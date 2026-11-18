"""Minimal newline-delimited JSON-RPC server used by the gateway tests.

Answers every request with the method it received and appends each tools/call
name to the file given in FAKE_SERVER_LOG, proving whether a call was forwarded.
"""
import json
import os
import sys

log_path = os.environ.get("FAKE_SERVER_LOG")
for line in sys.stdin:
    if not line.strip():
        continue
    message = json.loads(line)
    if message.get("method") == "tools/call" and log_path:
        with open(log_path, "a", encoding="utf-8") as handle:
            handle.write(message["params"]["name"] + "\n")
    if "id" in message:
        reply = {"jsonrpc": "2.0", "id": message["id"],
                 "result": {"echo": message.get("method"), "channel": os.environ.get("CONHUB_CHANNEL")}}
        sys.stdout.write(json.dumps(reply) + "\n")
        sys.stdout.flush()
