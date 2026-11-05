import json
import sys
import threading
import time
from pathlib import Path

events = Path(sys.argv[1])
lock = threading.Lock()
write_lock = threading.Lock()

def event(kind, **values):
    with lock:
        with events.open('a', encoding='utf-8') as handle:
            handle.write(json.dumps({'kind': kind, **values}) + '\n')

def respond(request):
    index = request.get('params', {}).get('index', 0)
    # Legal JSON-RPC: slow requests may complete after later fast requests.
    time.sleep(0.35 if index % 2 == 0 else 0.03)
    message = {'jsonrpc': '2.0', 'id': request['id'], 'result': {'ok': True, 'index': index}}
    try:
        with write_lock:
            sys.stdout.write(json.dumps(message) + '\n')
            sys.stdout.flush()
        event('sent', rpc_id=request['id'])
    except OSError as exc:
        event('write_error', error=repr(exc), errno=exc.errno, rpc_id=request['id'])

for line in sys.stdin:
    request = json.loads(line)
    event('received', rpc_id=request.get('id'), method=request.get('method'))
    if 'id' in request:
        threading.Thread(target=respond, args=(request,), daemon=True).start()
event('stdin_eof')
time.sleep(0.5)
