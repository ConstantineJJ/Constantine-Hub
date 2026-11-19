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


RESTART_ID_PREFIX = "claudius-restart-"
HANDSHAKE_SECONDS = 30.0


def restart_generation(config: dict[str, Any]) -> int:
    value = config.get("restart_generation", 0)
    return value if isinstance(value, int) and not isinstance(value, bool) and value >= 0 else 0


def spawn_child(config: dict[str, Any]) -> subprocess.Popen:
    command, cwd = child_command(config)
    env = dict(os.environ)
    env.setdefault("PYTHONUNBUFFERED", "1")
    env["CONHUB_CHANNEL"] = "claudius"
    return subprocess.Popen(command, cwd=cwd, env=env, stdin=subprocess.PIPE,
                            stdout=subprocess.PIPE, stderr=None, bufsize=0)


class Gateway:
    """Relays JSON-RPC and can replace its server process on a ConHub restart request.

    A restart (``restart_generation`` raised in the channel config) is applied only while
    no forwarded request is awaiting its response. The new server receives the client's
    original ``initialize`` (answered internally, never shown to Claude) and
    ``notifications/initialized`` before it serves traffic; the old one is then stopped.
    If the handshake fails, the old server keeps serving and the error is recorded.
    """

    def __init__(self, channel: Channel, child: subprocess.Popen, generation: int) -> None:
        self.channel = channel
        self.child = child
        self.out_lock = threading.Lock()
        self.child_lock = threading.RLock()
        self.stopping = threading.Event()
        self.pending: set[str] = set()
        self.applied_generation = generation
        self.init_message: dict[str, Any] | None = None
        self.handshakes: dict[str, threading.Event] = {}

    def send_client(self, data: bytes) -> None:
        with self.out_lock:
            sys.stdout.buffer.write(data)
            sys.stdout.buffer.flush()

    def write_child(self, child: subprocess.Popen, message: Any) -> None:
        stdin = child.stdin
        assert stdin is not None
        stdin.write((json.dumps(message) + "\n").encode("utf-8"))
        stdin.flush()

    def track(self, message: Any) -> None:
        for item in message if isinstance(message, list) else [message]:
            if isinstance(item, dict) and "method" in item and "id" in item:
                self.pending.add(json.dumps(item["id"]))

    def filter_message(self, message: Any) -> Any | None:
        """Return what to forward to the server, answering closed tool calls locally."""
        if isinstance(message, list):
            kept = [item for item in (self.filter_message(m) for m in message) if item is not None]
            return kept or None
        if not isinstance(message, dict):
            return message
        method = message.get("method")
        if method == "initialize":
            self.init_message = dict(message)
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

    def maybe_restart(self) -> None:
        generation = restart_generation(self.channel.config())
        if generation <= self.applied_generation or self.stopping.is_set():
            return
        with self.child_lock:
            if self.pending or generation <= self.applied_generation:
                return  # retried on the next message or heartbeat
            self.applied_generation = generation
            try:
                child = spawn_child(self.channel.config())
            except (OSError, FileNotFoundError) as error:
                log(f"restart {generation} failed to spawn: {error}")
                self.channel.update(restart_generation_applied=generation, restart_error=str(error))
                return
            threading.Thread(target=self.pump_server, args=(child,), daemon=True).start()
            if self.init_message is not None:
                key = RESTART_ID_PREFIX + str(generation)
                done = self.handshakes[key] = threading.Event()
                handshake = dict(self.init_message, id=key)
                try:
                    self.write_child(child, handshake)
                    ok = done.wait(HANDSHAKE_SECONDS)
                    if ok:
                        self.write_child(child, {"jsonrpc": "2.0", "method": "notifications/initialized"})
                except (OSError, ValueError):
                    ok = False
                self.handshakes.pop(key, None)
                if not ok:
                    log(f"restart {generation}: new server did not complete initialize; keeping pid {self.child.pid}")
                    threading.Thread(target=stop_child, args=(child, 0.5), daemon=True).start()
                    self.channel.update(restart_generation_applied=generation,
                                        restart_error="new server did not complete initialize")
                    return
            old, self.child = self.child, child
            restarts = int(self.channel.state.get("restarts") or 0) + 1
            self.channel.update(child_pid=child.pid, child_started_utc=utc_now(), restarts=restarts,
                                restart_generation_applied=generation, restart_error=None)
            log(f"restart {generation}: server pid {old.pid} -> {child.pid}")
        try:
            if old.stdin:
                old.stdin.close()
        except OSError:
            pass
        threading.Thread(target=stop_child, args=(old,), daemon=True).start()

    def pump_client(self) -> None:
        try:
            for line in sys.stdin.buffer:
                if not line.strip():
                    continue
                try:
                    message = json.loads(line)
                except ValueError:
                    with self.child_lock:
                        stdin = self.child.stdin
                        assert stdin is not None
                        stdin.write(line)  # let the server report malformed input itself
                        stdin.flush()
                    continue
                forward = self.filter_message(message)
                if forward is None:
                    continue
                self.maybe_restart()
                with self.child_lock:
                    self.track(forward)
                    self.write_child(self.child, forward)
        except (OSError, ValueError):
            pass
        finally:
            self.stopping.set()
            try:
                with self.child_lock:
                    if self.child.stdin:
                        self.child.stdin.close()
            except OSError:
                pass

    def pump_server(self, child: subprocess.Popen) -> None:
        stdout = child.stdout
        assert stdout is not None
        try:
            for line in stdout:
                try:
                    message = json.loads(line)
                except ValueError:
                    message = None
                if isinstance(message, dict) and "id" in message and "method" not in message:
                    key = message["id"]
                    if isinstance(key, str) and key in self.handshakes:
                        self.handshakes[key].set()  # internal handshake: never shown to the client
                        continue
                    with self.child_lock:
                        self.pending.discard(json.dumps(key))
                if child is not self.child:
                    continue  # a replaced server must not leak late output to the client
                self.send_client(line)
        except (OSError, ValueError):
            pass
        finally:
            if child is self.child:
                self.stopping.set()

    def heartbeat(self) -> None:
        while not self.stopping.wait(HEARTBEAT_SECONDS):
            self.channel.update(channel_open=self.channel.is_open())
            self.maybe_restart()


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
        child = spawn_child(channel.config())
    except FileNotFoundError as error:
        log(str(error))
        channel.update(stopped_utc=utc_now(), child_exit_code=-1)
        return 2

    generation = restart_generation(channel.config())
    channel.update(child_pid=child.pid, child_started_utc=utc_now(), channel_open=channel.is_open(),
                   restarts=0, restart_generation_applied=generation, restart_error=None)
    log(f"started pid={os.getpid()} child={child.pid} channel_open={channel.is_open()}")

    gateway = Gateway(channel, child, generation)
    threading.Thread(target=gateway.pump_server, args=(child,), daemon=True).start()
    for target in (gateway.pump_client, gateway.heartbeat):
        threading.Thread(target=target, daemon=True).start()
    gateway.stopping.wait()
    code = stop_child(gateway.child)
    channel.update(stopped_utc=utc_now(), child_exit_code=code)
    log(f"stopped child_exit={code}")
    return 0 if code in (0, None) else int(code)


if __name__ == "__main__":
    sys.exit(main())
