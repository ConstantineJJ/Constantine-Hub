"""ConHub Claudius channel: stdio MCP gateway between Claude Desktop and Blender MCP_Con.

Claude Desktop owns this process; ConHub owns the on/off switch (``enabled`` in the
channel config). The gateway relays newline-delimited JSON-RPC unchanged in both
directions. While the channel is closed, every ``tools/call`` is answered here with
an MCP tool error and is never forwarded, so nothing reaches Blender or Tools_C.
Discovery (initialize, tools/list) keeps working so the client catalog stays stable.

Unreadable or missing config fails closed. Standard library only.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import threading
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

SCHEMA_VERSION = 1
CLOSED_CODE = "CLAUDIUS_CHANNEL_CLOSED"
HEARTBEAT_SECONDS = 5.0
CLOSED_MESSAGE = (
    CLOSED_CODE + ": the Claudius channel is switched off in ConHub. Nothing was sent to "
    "Blender or Tools_C. Ask the owner to press Start on the Claudius MCP card in ConHub, "
    "then retry. Jarvis channels are not affected by this switch."
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def default_config_path() -> Path:
    base = os.environ.get("APPDATA") or str(Path.home() / ".config")
    return Path(base) / "ConstantineHub" / "claudius.json"


def log(message: str) -> None:
    # stdout carries only JSON-RPC; diagnostics go to the client's MCP log via stderr.
    try:
        sys.stderr.write("[claudius-gateway] " + message + "\n")
        sys.stderr.flush()
    except (OSError, ValueError):
        pass


class Channel:
    """Reads the ConHub switch (cached by mtime) and publishes gateway state."""

    def __init__(self, config_path: Path) -> None:
        self.config_path = config_path
        self.state_path = config_path.with_name("claudius-state.json")
        self._mtime: int | None = None
        self._config: dict[str, Any] = {}
        self._lock = threading.Lock()
        self.state: dict[str, Any] = {
            "schema_version": SCHEMA_VERSION,
            "gateway_pid": os.getpid(),
            "child_pid": None,
            "started_utc": utc_now(),
            "heartbeat_utc": None,
            "stopped_utc": None,
            "client": None,
            "channel_open": False,
            "calls": 0,
            "blocked": 0,
            "last_call_utc": None,
            "last_tool": None,
            "child_exit_code": None,
        }

    def config(self) -> dict[str, Any]:
        try:
            mtime = self.config_path.stat().st_mtime_ns
        except OSError:
            self._mtime, self._config = None, {}
            return self._config
        if mtime != self._mtime:
            try:
                loaded = json.loads(self.config_path.read_text(encoding="utf-8-sig"))
                self._config = loaded if isinstance(loaded, dict) else {}
            except (OSError, ValueError):
                self._config = {}
            self._mtime = mtime
        return self._config

    def is_open(self) -> bool:
        return self.config().get("enabled") is True

    def update(self, **values: Any) -> None:
        with self._lock:
            self.state.update(values)
        self.write_state()

    def count_call(self, tool: str | None, allowed: bool) -> None:
        with self._lock:
            key = "calls" if allowed else "blocked"
            self.state[key] += 1
            self.state["last_call_utc"] = utc_now()
            self.state["last_tool"] = tool
            self.state["channel_open"] = allowed
        self.write_state()

    def write_state(self) -> None:
        with self._lock:
            self.state["heartbeat_utc"] = utc_now()
            payload = json.dumps(self.state, indent=2)
            temp = self.state_path.with_name(self.state_path.name + f".{os.getpid()}.tmp")
            try:
                self.state_path.parent.mkdir(parents=True, exist_ok=True)
                temp.write_text(payload + "\n", encoding="utf-8")
                os.replace(temp, self.state_path)
            except OSError as error:
                log(f"state write failed: {error}")


def child_command(config: dict[str, Any]) -> tuple[list[str], str]:
    raw = str(config.get("server_command") or "").strip().strip('"')
    if not raw:
        raise FileNotFoundError("server_command is not configured; press Start in ConHub.")
    path = Path(os.path.expandvars(raw)).resolve()
    if not path.is_file():
        raise FileNotFoundError(f"Blender MCP server command not found: {path}")
    extra = [str(item) for item in config.get("server_args") or []]
    suffix = path.suffix.lower()
    if suffix in (".cmd", ".bat"):
        shell = os.environ.get("ComSpec") or "cmd.exe"
        command = [shell, "/d", "/c", str(path), *extra]
    elif suffix == ".py":
        command = [sys.executable, str(path), *extra]
    else:
        command = [str(path), *extra]
    return command, str(path.parent)


def closed_response(request_id: Any) -> dict[str, Any]:
    return {
        "jsonrpc": "2.0",
        "id": request_id,
        "result": {"content": [{"type": "text", "text": CLOSED_MESSAGE}], "isError": True},
    }


class Gateway:
    def __init__(self, channel: Channel, child: subprocess.Popen) -> None:
        self.channel = channel
        self.child = child
        self.out_lock = threading.Lock()
        self.stopping = threading.Event()

    def send_client(self, data: bytes) -> None:
        with self.out_lock:
            sys.stdout.buffer.write(data)
            sys.stdout.buffer.flush()

    def filter_message(self, message: Any) -> Any | None:
        """Return what to forward to the server, answering closed tool calls locally."""
        if isinstance(message, list):
            kept = [item for item in (self.filter_message(m) for m in message) if item is not None]
            return kept or None
        if not isinstance(message, dict):
            return message
        method = message.get("method")
        if method == "initialize":
            info = (message.get("params") or {}).get("clientInfo") or {}
            name = " ".join(str(info.get(k, "")) for k in ("name", "version")).strip() or None
            self.channel.update(client=name)
        if method == "tools/call" and "id" in message:
            tool = (message.get("params") or {}).get("name")
            if not self.channel.is_open():
                self.channel.count_call(tool, allowed=False)
                self.send_client((json.dumps(closed_response(message["id"])) + "\n").encode("utf-8"))
                return None
            self.channel.count_call(tool, allowed=True)
        return message

    def pump_client(self) -> None:
        stdin = self.child.stdin
        assert stdin is not None
        try:
            for line in sys.stdin.buffer:
                if not line.strip():
                    continue
                try:
                    message = json.loads(line)
                except ValueError:
                    stdin.write(line)  # let the server report malformed input itself
                    stdin.flush()
                    continue
                forward = self.filter_message(message)
                if forward is None:
                    continue
                stdin.write((json.dumps(forward) + "\n").encode("utf-8"))
                stdin.flush()
        except (OSError, ValueError):
            pass
        finally:
            self.stopping.set()
            try:
                stdin.close()
            except OSError:
                pass

    def pump_server(self) -> None:
        stdout = self.child.stdout
        assert stdout is not None
        try:
            for line in stdout:
                self.send_client(line)
        except (OSError, ValueError):
            pass
        finally:
            self.stopping.set()

    def heartbeat(self) -> None:
        while not self.stopping.wait(HEARTBEAT_SECONDS):
            self.channel.update(channel_open=self.channel.is_open())


def stop_child(child: subprocess.Popen, grace: float = 5.0) -> int | None:
    try:
        return child.wait(timeout=grace)
    except subprocess.TimeoutExpired:
        pass
    if os.name == "nt":
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(child.pid)],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    else:
        child.kill()
    try:
        return child.wait(timeout=grace)
    except subprocess.TimeoutExpired:
        return None


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="ConHub Claudius MCP channel gateway")
    parser.add_argument("--config", type=Path, default=default_config_path())
    args = parser.parse_args(argv)

    channel = Channel(args.config.expanduser().resolve())
    try:
        command, cwd = child_command(channel.config())
    except FileNotFoundError as error:
        log(str(error))
        channel.update(stopped_utc=utc_now(), child_exit_code=-1)
        return 2

    env = dict(os.environ)
    env.setdefault("PYTHONUNBUFFERED", "1")
    env["CONHUB_CHANNEL"] = "claudius"
    child = subprocess.Popen(command, cwd=cwd, env=env, stdin=subprocess.PIPE,
                             stdout=subprocess.PIPE, stderr=None, bufsize=0)
    channel.update(child_pid=child.pid, channel_open=channel.is_open())
    log(f"started pid={os.getpid()} child={child.pid} channel_open={channel.is_open()}")

    gateway = Gateway(channel, child)
    for target in (gateway.pump_client, gateway.pump_server, gateway.heartbeat):
        threading.Thread(target=target, daemon=True).start()
    gateway.stopping.wait()
    code = stop_child(child)
    channel.update(stopped_utc=utc_now(), child_exit_code=code)
    log(f"stopped child_exit={code}")
    return 0 if code in (0, None) else int(code)


if __name__ == "__main__":
    sys.exit(main())
