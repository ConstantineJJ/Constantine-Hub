from __future__ import annotations

import asyncio
import json
import os
from pathlib import Path
import socket
import time
import uuid
from dataclasses import dataclass
from typing import Any

# Bound the engine frame before JSON parsing. MCP adds text/structured copies;
# the server separately checks the final serialized MCP result.
MAX_BRIDGE_BYTES = 512 * 1024
MAX_REQUEST_BYTES = 2 * 1024 * 1024

@dataclass(slots=True)
class BridgeEndpoint:
    host: str
    port: int
    name: str

class BridgeError(RuntimeError):
    pass

class JsonLineBridge:
    def __init__(self, endpoint: BridgeEndpoint, timeout: float = 4.0, project_root: Path | None = None) -> None:
        self.endpoint = endpoint
        self.timeout = timeout
        self.project_root = project_root

    def probe(self, timeout: float = 0.35) -> bool:
        try:
            result = self._call("status", timeout=timeout)
            if self.project_root is not None:
                self._validate_project(result)
            return True
        except (OSError, BridgeError):
            return False

    async def aprobe(self, timeout: float = 0.35) -> bool:
        try:
            result = await self._acall("status", timeout=timeout)
            if self.project_root is not None:
                self._validate_project(result)
            return True
        except (OSError, BridgeError):
            return False

    def call(self, op: str, **params: Any) -> Any:
        if self.project_root is not None:
            status = self._call("status")
            self._validate_project(status)
            if op == "status":
                return status
        return self._call(op, **params)

    async def acall(self, op: str, **params: Any) -> Any:
        if self.project_root is not None:
            status = await self._acall("status")
            self._validate_project(status)
            if op == "status":
                return status
        return await self._acall(op, **params)

    def _validate_project(self, result: Any) -> None:
        reported = result.get("project_path") if isinstance(result, dict) else None
        if not isinstance(reported, str) or not reported or os.path.normcase(os.path.realpath(reported)) != os.path.normcase(os.path.realpath(self.project_root)):
            raise BridgeError(f"{self.endpoint.name} belongs to another project ({reported or 'unknown'}); selected project is {self.project_root}. Open the selected project in Godot.")

    def _request(self, op: str, params: dict[str, Any]) -> tuple[str, bytes]:
        request_id = uuid.uuid4().hex
        payload = {"id": request_id, "op": op, "params": params}
        if self.project_root is not None:
            payload["project_root"] = str(self.project_root)
        raw = (json.dumps(payload, ensure_ascii=False) + "\n").encode("utf-8")
        if len(raw) > MAX_REQUEST_BYTES:
            raise BridgeError(f"{self.endpoint.name} request exceeds {MAX_REQUEST_BYTES} bytes; split the operation")
        return request_id, raw

    def _result(self, line: bytes, request_id: str) -> Any:
        if not line or not line.endswith(b"\n"):
            raise BridgeError(f"{self.endpoint.name} bridge closed before a complete response")
        if len(line) > MAX_BRIDGE_BYTES:
            raise BridgeError(f"{self.endpoint.name} response exceeds {MAX_BRIDGE_BYTES} bytes; use scene-tree pagination or a property filter")
        try:
            response = json.loads(line.decode("utf-8"))
        except (ValueError, UnicodeError) as exc:
            raise BridgeError(f"{self.endpoint.name} bridge returned invalid JSON") from exc
        if not isinstance(response, dict) or response.get("id") != request_id:
            raise BridgeError(f"{self.endpoint.name} bridge response id missing or mismatched")
        if response.get("ok") is not True:
            raise BridgeError(str(response.get("error") or "Bridge operation failed")[:2000])
        return response.get("result")

    def _call(self, op: str, *, timeout: float | None = None, **params: Any) -> Any:
        request_id, raw = self._request(op, params)
        request_timeout = self.timeout if timeout is None else timeout
        deadline = time.monotonic() + request_timeout
        data = bytearray()
        try:
            with socket.create_connection((self.endpoint.host, self.endpoint.port), timeout=request_timeout) as sock:
                sock.settimeout(max(0.001, deadline - time.monotonic()))
                sock.sendall(raw)
                while b"\n" not in data:
                    remaining = deadline - time.monotonic()
                    if remaining <= 0:
                        raise TimeoutError("response deadline exceeded")
                    sock.settimeout(remaining)
                    block = sock.recv(min(65536, MAX_BRIDGE_BYTES + 1 - len(data)))
                    if not block:
                        break
                    data.extend(block)
                    if len(data) > MAX_BRIDGE_BYTES:
                        raise BridgeError(f"{self.endpoint.name} response exceeds {MAX_BRIDGE_BYTES} bytes; use scene-tree pagination or a property filter")
        except OSError as exc:
            raise BridgeError(f"{self.endpoint.name} bridge {op} at {self.endpoint.host}:{self.endpoint.port} failed: {exc}") from exc
        return self._result(bytes(data).split(b"\n", 1)[0] + (b"\n" if b"\n" in data else b""), request_id)

    async def _acall(self, op: str, *, timeout: float | None = None, **params: Any) -> Any:
        request_id, raw = self._request(op, params)
        writer: asyncio.StreamWriter | None = None
        try:
            # One disposable socket per request: cancellation/disconnect cannot
            # leave a pending response to be mistaken for a later request.
            async with asyncio.timeout(self.timeout if timeout is None else timeout):
                reader, writer = await asyncio.open_connection(self.endpoint.host, self.endpoint.port, limit=MAX_BRIDGE_BYTES)
                writer.write(raw)
                await writer.drain()
                line = await reader.readline()
                return self._result(line, request_id)
        except (OSError, TimeoutError, ValueError) as exc:
            raise BridgeError(f"{self.endpoint.name} bridge {op} at {self.endpoint.host}:{self.endpoint.port} failed: {exc}") from exc
        finally:
            if writer is not None:
                writer.close()
                # close() releases the transport even if the request is cancelled.
                try:
                    await writer.wait_closed()
                except (OSError, asyncio.CancelledError):
                    pass
