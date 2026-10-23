"""Real MCP clients over stdio and Streamable HTTP; no project build is invoked."""
import hashlib
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
from concurrent.futures import ThreadPoolExecutor


def check(ok, message):
    if not ok:
        raise AssertionError(message)
    print("PASS:", message)


def payload(method, params=None, identifier=1):
    return {"jsonrpc": "2.0", "id": identifier, "method": method, "params": params or {}}


INIT = payload("initialize", {"protocolVersion": "2025-11-25", "capabilities": {},
                              "clientInfo": {"name": "helper-acceptance", "version": "1.0"}})


def decoded(reply):
    check("error" not in reply, "MCP request succeeded")
    result = reply["result"]
    check(not result.get("isError"), "tool did not return a protocol error")
    return json.loads(next(x["text"] for x in result["content"] if x["type"] == "text"))


def exercise(request):
    initialized = request(INIT)
    check("serverInfo" in initialized["result"], "real MCP initialize")
    tools = request(payload("tools/list"))["result"]["tools"]
    check(len(tools) == 7 and all(t["annotations"]["readOnlyHint"] for t in tools), "seven read-only tools; apply absent")
    status = decoded(request(payload("tools/call", {"name": "helper_status", "arguments": {}})))
    check(status["version"] == "0.1.0" and not status["apply_available"], "standalone version and no apply")
    found = decoded(request(payload("tools/call", {"name": "helper_find_symbol", "arguments": {"query": "Echo"}})))
    check(len(found["results"]) == 2, "semantic overloads through actual MCP transport")
    symbol = next(x for x in found["results"] if "int" in x["signature"])
    args = {"symbolId": symbol["symbol_id"], "expectedSnapshot": found["snapshot_id"]}
    refs = decoded(request(payload("tools/call", {"name": "helper_find_references", "arguments": args})))
    check(refs["total"] == 3 and refs["analysis_coverage"] == "partial", "cross-project references and honest coverage")
    preview = decoded(request(payload("tools/call", {"name": "helper_refactor_preview", "arguments": dict(args, newName="Repeat")})))
    check(preview["total"] == 4 and len(preview["edits"]) == 3, "exact rename preview without apply")
    invalid = decoded(request(payload("tools/call", {"name": "helper_outline", "arguments": {"file": "../outside.cs"}})))
    check(invalid["error_code"] == "E_SCOPE", "transport cannot escape workspace")


def run(executable):
    with tempfile.TemporaryDirectory(prefix="mcp-helper-protocol-") as temp:
        root = Path(temp)
        workspace = root / "workspace"
        workspace.mkdir()
        for name in ("Library", "App"):
            (workspace / name).mkdir()
        (workspace / "Library/Library.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><Target Name="NoExecution" BeforeTargets="Build"><Error Text="MUST NOT EXECUTE" /></Target></Project>')
        (workspace / "App/App.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup></Project>')
        (workspace / "Library/Service.cs").write_text('namespace Demo; public partial class Service { public int Echo(int n) => n; public string Echo(string s) => s; }')
        (workspace / "Library/Other.cs").write_text('namespace Demo; public partial class Service { public int Twice() => Echo(1) + Echo(2); }')
        (workspace / "App/Caller.cs").write_text('public class Caller { public int Run() => new Demo.Service().Echo(3); }')
        before = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in workspace.rglob("*") if p.is_file()}
        token = secrets.token_hex(32)
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            port = sock.getsockname()[1]
        config = root / "config.json"
        config.write_text(json.dumps({"WorkspaceRoot": str(workspace), "Token": token, "Port": port, "InstanceId": "acceptance"}))
        command = [str(executable)] if str(executable).endswith(".exe") else ["dotnet", str(executable)]
        flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
        with (root / "stdio.log").open("w") as log:
            process = subprocess.Popen(command + ["stdio", "--config", str(config)], stdin=subprocess.PIPE,
                                       stdout=subprocess.PIPE, stderr=log, text=True, encoding="utf-8", creationflags=flags)
            pool = ThreadPoolExecutor(max_workers=1)
            try:
                def stdio(request):
                    process.stdin.write(json.dumps(request) + "\n")
                    process.stdin.flush()
                    while True:
                        line = pool.submit(process.stdout.readline).result(timeout=40)
                        if not line:
                            raise RuntimeError("Helper exited before replying")
                        reply = json.loads(line)  # stdout must contain protocol only.
                        if reply.get("id") == request["id"]:
                            return reply
                exercise(stdio)
            finally:
                process.terminate()
                process.wait(timeout=10)
                pool.shutdown(wait=True)
        with (root / "http.log").open("w") as log:
            process = subprocess.Popen(command + ["serve", "--config", str(config)], stdout=log, stderr=log, creationflags=flags)
            endpoint = f"http://127.0.0.1:{port}/mcp"

            def http(request, auth=token, origin=None, host=None):
                headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream",
                           "Authorization": "Bearer " + auth, "MCP-Protocol-Version": "2025-11-25"}
                if origin:
                    headers["Origin"] = origin
                if host:
                    headers["Host"] = host
                req = urllib.request.Request(endpoint, json.dumps(request).encode(), headers)
                with urllib.request.urlopen(req, timeout=40) as response:
                    text = response.read().decode()
                    if response.headers.get_content_type() == "text/event-stream":
                        return next(json.loads(line[5:].strip()) for line in text.splitlines() if line.startswith("data:") and line[5:].strip().startswith("{"))
                    return json.loads(text)
            try:
                for _ in range(80):
                    try:
                        http(INIT)
                        break
                    except urllib.error.URLError:
                        if process.poll() is not None:
                            raise RuntimeError("HTTP Helper failed to start")
                        time.sleep(0.1)
                else:
                    raise RuntimeError("HTTP startup timeout")
                exercise(http)
                for kwargs, expected in [({"auth": "wrong"}, 401), ({"origin": "https://evil.example"}, 403), ({"host": "evil.example"}, 403)]:
                    try:
                        http(INIT, **kwargs)
                        raise AssertionError("Untrusted request was accepted")
                    except urllib.error.HTTPError as error:
                        check(error.code == expected, f"HTTP boundary rejects request: {expected}")
            finally:
                process.terminate()
                process.wait(timeout=10)
        after = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in workspace.rglob("*") if p.is_file()}
        check(before == after, "all workspace bytes and membership unchanged")
        for log in root.glob("*.log"):
            check(token not in log.read_text(encoding="utf-8"), "auth token absent from process logs")
        print("PROTOCOL PASS: STDIO + STREAMABLE HTTP")


if __name__ == "__main__":
    run(Path(sys.argv[1]).resolve())
