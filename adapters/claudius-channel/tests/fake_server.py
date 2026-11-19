"""Minimal newline-delimited JSON-RPC server used by the gateway tests.

Answers every request with the method it received and its own pid, and appends each
tools/call name (and '@initialize' for handshakes) to FAKE_SERVER_LOG, proving whether
a call was forwarded and which server process received the MCP handshake.
"""
import json
import os
import sys

log_path = os.environ.get("FAKE_SERVER_LOG")


def note(text):
    if log_path:
        with open(log_path, "a", encoding="utf-8") as handle:
            handle.write(text + "\n")


for line in sys.stdin:
    if not line.strip():
        continue
    message = json.loads(line)
    if message.get("method") == "tools/call":
        note(message["params"]["name"])
    if message.get("method") == "initialize":
        note("@initialize")
    if "id" in message:
        reply = {"jsonrpc": "2.0", "id": message["id"],
                 "result": {"echo": message.get("method"), "channel": os.environ.get("CONHUB_CHANNEL"),
                            "pid": os.getpid()}}
        sys.stdout.write(json.dumps(reply) + "\n")
        sys.stdout.flush()
