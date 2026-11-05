"""Bounded loopback-only reproduction against the installed tunnel binary."""
import argparse
import datetime
import hashlib
import http.server
import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import tempfile
import time
import urllib.request
import urllib.parse

ROOT = Path(__file__).resolve().parent
BINARY = None
OUTPUT = None
TUNNEL = 'tunnel_00000000000000000000000000000000'

def run_case(name, inflight, concurrent=None):
    directory = OUTPUT / name
    directory.mkdir(exist_ok=True)
    (directory / 'profiles-empty').mkdir(exist_ok=True)
    (directory / 'empty.yaml').write_text('{}\n', encoding='utf-8')
    events = directory / 'mcp-events.jsonl'
    if events.exists():
        raise RuntimeError('Use a fresh case name; evidence must not be overwritten')
    responses = []
    requests = []
    next_command = 0
    gate = threading.Lock()
    stamp = datetime.datetime.now(datetime.timezone.utc).isoformat().replace('+00:00', 'Z')
    commands = [dict(command_type='jsonrpc', request_id=f'cmd-{i}', shard_token=f'shard-{i}',
                     created_at=stamp, headers={}, channel='main',
                     jsonrpc=dict(jsonrpc='2.0', id=f'rpc-{i}', method='ping', params={'index': i}))
                for i in range(8)]

    class Handler(http.server.BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def send_json(self, value):
            raw = json.dumps(value).encode()
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(raw)))
            try:
                self.end_headers()
                self.wfile.write(raw)
            except OSError:
                pass

        def do_GET(self):
            nonlocal next_command
            with gate:
                requests.append(self.path)
            if self.path.split('?')[0].endswith('/poll'):
                with gate:
                    query = urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query)
                    limit = int(query.get('limit', ['20'])[0])
                    payload = commands[next_command:next_command + limit]
                    next_command += len(payload)
                if not payload:
                    time.sleep(0.1)
                self.send_json({'commands': payload})
            else:
                self.send_json({'id': TUNNEL, 'object': 'tunnel', 'name': 'isolated-repro'})

        def do_POST(self):
            raw = self.rfile.read(int(self.headers.get('Content-Length', '0')))
            with gate:
                responses.append(json.loads(raw))
                requests.append(self.path)
            self.send_json({})

    server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    server.daemon_threads = True
    threading.Thread(target=server.serve_forever, daemon=True).start()
    base_url = f'http://127.0.0.1:{server.server_port}'
    # Deliberately do not inherit credentials, proxy variables or tunnel settings.
    env = {key: os.environ[key] for key in ('SystemRoot', 'WINDIR', 'TEMP', 'TMP', 'COMSPEC', 'PATH') if key in os.environ}
    env.update(CONHUB_REPRO_KEY='isolated-dummy-key', APPDATA=str(directory), LOCALAPPDATA=str(directory),
               USERPROFILE=str(directory), NO_PROXY='*', PYTHONIOENCODING='utf-8')
    health_file = directory / 'health.url'
    args = [str(BINARY), 'run', '--profile-dir', str(directory / 'profiles-empty'), '--config', str(directory / 'empty.yaml'),
            '--control-plane.base-url', base_url, '--control-plane.tunnel-id', TUNNEL,
            '--control-plane.api-key', 'env:CONHUB_REPRO_KEY', '--health.listen-addr', '127.0.0.1:0',
            '--health.url-file', str(health_file), '--mcp.command', f'"{Path(sys.executable).as_posix()}" "{(ROOT / "stdio_fake_server.py").as_posix()}" "{events.as_posix()}"',
            '--log.format', 'json', '--log.level', 'debug']
    if inflight is not None:
        args.extend(['--control-plane.max-inflight', str(inflight)])
    if concurrent is not None:
        args.extend(['--mcp.max-concurrent-requests', str(concurrent)])
    (directory / 'isolation.json').write_text(json.dumps(dict(args=args, env_keys=sorted(env),
        control_plane=base_url, health='127.0.0.1:0', binary_sha256=hashlib.sha256(BINARY.read_bytes()).hexdigest()), indent=2))
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    process = None
    health = None
    with (directory / 'tunnel.log').open('w', encoding='utf-8') as log:
        try:
            process = subprocess.Popen(args, env=env, cwd=directory, stdout=log, stderr=subprocess.STDOUT,
                                       creationflags=subprocess.CREATE_NO_WINDOW)
            started = time.monotonic()
            while time.monotonic() - started < 10 and process.poll() is None:
                if len(responses) >= 8:
                    break
                time.sleep(0.05)
            if health_file.exists():
                health_url = health_file.read_text().strip()
                assert health_url.startswith('http://127.0.0.1:')
                with opener.open(health_url + '/api/status', timeout=2) as reply:
                    health = json.load(reply)
                with opener.open(health_url + '/readyz', timeout=2) as reply:
                    ready = dict(code=reply.status, body=reply.read().decode())
            else:
                ready = None
        finally:
            if process is not None and process.poll() is None:
                subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], stdout=subprocess.DEVNULL,
                               stderr=subprocess.DEVNULL, timeout=5, creationflags=subprocess.CREATE_NO_WINDOW)
                process.wait(timeout=5)
            server.shutdown()
            server.server_close()
    log_text = (directory / 'tunnel.log').read_text(encoding='utf-8')
    fake_events = [json.loads(row) for row in events.read_text().splitlines()] if events.exists() else []
    result = dict(case=name, max_inflight=inflight, max_concurrent=concurrent, received_response_count=len(responses), responses=responses,
                  invalid_id='Received response without valid ID' in log_text,
                  closed_pipe='file already closed' in log_text,
                  degraded_exit='stdio MCP command exited; tunnel-client remains running in degraded mode' in log_text,
                  fake_events=fake_events, ready=ready, health=health, http_paths=sorted(set(requests)))
    (directory / 'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('case', 'received_response_count', 'invalid_id', 'closed_pipe', 'degraded_exit', 'ready')}))
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--binary', type=Path, required=True, help='Local tunnel-client executable to test')
    parser.add_argument('--output', type=Path, help='New evidence directory; existing directories are refused')
    parser.add_argument('--safe-only', action='store_true', help='Skip diagnostic unsafe baseline cases')
    args = parser.parse_args()
    if os.name != 'nt':
        parser.error('This test validates Windows stdio behavior and requires Windows')
    BINARY = args.binary.resolve(strict=True)
    if not BINARY.is_file():
        parser.error('--binary must name a local executable file')
    OUTPUT = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix='conhub-stdio-evidence-'))
    if args.output:
        OUTPUT.mkdir(parents=True, exist_ok=False)
    print('Evidence:', OUTPUT)
    # Baseline behavior is diagnostic: future fixed binaries need not fail.
    if not args.safe_only:
        run_case('default', None)
        run_case('single-inflight', 1)
    safe = run_case('single-worker', None, 1)
    assert safe['received_response_count'] == 8, 'Single worker did not deliver all requests'
    assert not any(safe[key] for key in ('invalid_id', 'closed_pipe', 'degraded_exit')), 'Single-worker transport failed'
    assert not any(row['kind'] == 'write_error' for row in safe['fake_events']), 'Fake MCP stdout failed'
    expected = {f'cmd-{i}': f'rpc-{i}' for i in range(8)}
    actual = {row['request_id']: row for row in safe['responses']}
    assert set(actual) == set(expected), 'Missing or duplicate correlated responses'
    for request_id, rpc_id in expected.items():
        row = actual[request_id]
        assert row['resp_code'] == 200, row
        assert row['resp_json']['id'] == rpc_id and row['resp_json'].get('result', {}).get('ok') is True, row
    print('PASS: one worker delivers 8 correlated HTTP 200 responses without closing stdio.')
