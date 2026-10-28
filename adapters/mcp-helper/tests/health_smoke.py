"""Authenticated idle health checks must neither analyze sources nor flood Hub logs."""
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request


def run(executable):
    with tempfile.TemporaryDirectory(prefix="helper-health-") as temp:
        root = Path(temp)
        workspace = root / "workspace"
        workspace.mkdir()
        # Semantic analysis would reject this file; health must not read it.
        (workspace / "Unreadable.cs").write_bytes(b"\xff\xfe\x00\x00")
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            port = sock.getsockname()[1]
        token = secrets.token_hex(32)
        config = root / "config.json"
        config.write_text(json.dumps(dict(WorkspaceRoot=str(workspace), Port=port,
                                         Token=token, InstanceId="idle-health")))
        command = [str(executable)] if executable.suffix == ".exe" else ["dotnet", str(executable)]
        log_path = root / "service.log"

        def health(auth=token, **headers):
            request = urllib.request.Request(f"http://127.0.0.1:{port}/health",
                                             headers={"Authorization": "Bearer " + auth, **headers})
            with urllib.request.urlopen(request, timeout=3) as response:
                assert response.headers["Cache-Control"] == "no-store"
                return json.load(response)

        with log_path.open("w", encoding="utf-8") as log:
            process = subprocess.Popen(command + ["serve", "--config", str(config)], stdout=log,
                                       stderr=log, creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            try:
                for _ in range(80):
                    try:
                        status = health()
                        break
                    except urllib.error.HTTPError:
                        raise
                    except urllib.error.URLError:
                        if process.poll() is not None:
                            raise AssertionError("Helper exited before health became available")
                        time.sleep(0.1)
                else:
                    raise AssertionError("Helper health startup timed out")
                assert status["instance_id"] == "idle-health"
                assert status["version"] == "0.1.2"
                assert Path(status["workspace_root"]) == workspace
                assert status["mcp_ready"] and not status["apply_available"]
                for _ in range(60):
                    assert health() == status
                for kwargs, expected in [({"auth": "wrong"}, 401),
                                         ({"Origin": "https://evil.example"}, 403),
                                         ({"Host": "evil.example"}, 403)]:
                    try:
                        health(**kwargs)
                    except urllib.error.HTTPError as error:
                        assert error.code == expected
                    else:
                        raise AssertionError("Unauthenticated or untrusted health request accepted")
            finally:
                process.terminate()
                process.wait(timeout=10)
        logs = log_path.read_text(encoding="utf-8")
        assert token not in logs
        assert len(logs.splitlines()) <= 12, "Idle health requests flooded process logs"
        print("HEALTH PASS: 60 lightweight probes, identity, authorization, origin/host, bounded idle logging")


if __name__ == "__main__":
    run(Path(sys.argv[1]).resolve())
