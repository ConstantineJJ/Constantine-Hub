from __future__ import annotations

import json
import os
from pathlib import Path
import socket
import uuid
from dataclasses import dataclass
from typing import Any

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
            if self.project_root is not None:
                self._validate_project(self._call("status", timeout=timeout))
                return True
            with socket.create_connection((self.endpoint.host, self.endpoint.port), timeout=timeout):
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

    def _validate_project(self, result: Any) -> None:
        reported = result.get("project_path") if isinstance(result, dict) else None
        if not isinstance(reported, str) or not reported or os.path.normcase(os.path.realpath(reported)) != os.path.normcase(os.path.realpath(self.project_root)):
            raise BridgeError(f"{self.endpoint.name} belongs to another project ({reported or 'unknown'}); selected project is {self.project_root}. Open the selected project in Godot.")

    def _call(self, op: str, *, timeout: float | None = None, **params: Any) -> Any:
        request_id = uuid.uuid4().hex
        payload = {"id": request_id, "op": op, "params": params}
        if self.project_root is not None:
            payload["project_root"] = str(self.project_root)
        raw = (json.dumps(payload, ensure_ascii=False) + "\n").encode("utf-8")
        request_timeout = self.timeout if timeout is None else timeout
        try:
            with socket.create_connection((self.endpoint.host, self.endpoint.port), timeout=request_timeout) as sock:
                sock.settimeout(request_timeout)
                sock.sendall(raw)
                chunks: list[bytes] = []
                while True:
                    block = sock.recv(65536)
                    if not block:
                        break
                    chunks.append(block)
                    if b"\n" in block:
                        break
        except OSError as exc:
            raise BridgeError(f"{self.endpoint.name} bridge {self.endpoint.host}:{self.endpoint.port} is unavailable: {exc}") from exc

        if not chunks:
            raise BridgeError(f"{self.endpoint.name} bridge returned no response")
        line = b"".join(chunks).split(b"\n", 1)[0]
        try:
            response = json.loads(line.decode("utf-8"))
        except Exception as exc:
            raise BridgeError(f"{self.endpoint.name} bridge returned invalid JSON") from exc
        if response.get("id") not in (None, request_id):
            raise BridgeError(f"{self.endpoint.name} bridge response id mismatch")
        if not response.get("ok", False):
            raise BridgeError(str(response.get("error") or "Bridge operation failed"))
        return response.get("result")
