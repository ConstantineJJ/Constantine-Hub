from __future__ import annotations

import json
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
    def __init__(self, endpoint: BridgeEndpoint, timeout: float = 4.0) -> None:
        self.endpoint = endpoint
        self.timeout = timeout

    def probe(self, timeout: float = 0.35) -> bool:
        try:
            with socket.create_connection((self.endpoint.host, self.endpoint.port), timeout=timeout):
                return True
        except OSError:
            return False

    def call(self, op: str, **params: Any) -> Any:
        request_id = uuid.uuid4().hex
        payload = {"id": request_id, "op": op, "params": params}
        raw = (json.dumps(payload, ensure_ascii=False) + "\n").encode("utf-8")
        try:
            with socket.create_connection((self.endpoint.host, self.endpoint.port), timeout=self.timeout) as sock:
                sock.settimeout(self.timeout)
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
