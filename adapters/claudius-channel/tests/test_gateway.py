"""Regression tests for the ConHub Claudius channel gateway (stdlib only)."""
import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
GATEWAY = HERE.parent / "claudius_gateway.py"
FAKE = HERE / "fake_server.py"


class GatewayTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.config = self.root / "claudius.json"
        self.state = self.root / "claudius-state.json"
        self.calls = self.root / "forwarded.txt"
        self.set_open(False)

    def tearDown(self):
        self.temp.cleanup()

    def set_open(self, enabled, server=FAKE):
        payload = {"schema_version": 1, "enabled": enabled, "server_command": str(server)}
        self.config.write_text(json.dumps(payload), encoding="utf-8")
        # Guarantee a new mtime even on coarse file-system clocks.
        stamp = time.time_ns() + 1_000_000
        os.utime(self.config, ns=(stamp, stamp))

    def start(self):
        env = dict(os.environ, FAKE_SERVER_LOG=str(self.calls))
        return subprocess.Popen([sys.executable, str(GATEWAY), "--config", str(self.config)],
                                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                env=env, bufsize=0)

    @staticmethod
    def request(process, request_id, method, params=None):
        message = {"jsonrpc": "2.0", "id": request_id, "method": method, "params": params or {}}
        process.stdin.write((json.dumps(message) + "\n").encode("utf-8"))
        process.stdin.flush()
        return json.loads(process.stdout.readline())

    def finish(self, process):
        process.stdin.close()
        self.assertEqual(process.wait(timeout=20), 0, process.stderr.read().decode("utf-8", "replace"))
        process.stdout.close()
        process.stderr.close()

    def forwarded(self):
        return self.calls.read_text(encoding="utf-8").split() if self.calls.exists() else []

    def test_closed_channel_answers_locally_and_discovery_still_works(self):
        process = self.start()
        init = self.request(process, 1, "initialize", {"clientInfo": {"name": "claude-ai", "version": "1"}})
        self.assertEqual(init["result"]["echo"], "initialize")
        self.assertEqual(init["result"]["channel"], "claudius")
        self.assertEqual(self.request(process, 2, "tools/list")["result"]["echo"], "tools/list")
        blocked = self.request(process, 3, "tools/call", {"name": "execute_blender_python", "arguments": {}})
        self.assertTrue(blocked["result"]["isError"])
        self.assertIn("CLAUDIUS_CHANNEL_CLOSED", blocked["result"]["content"][0]["text"])
        self.assertEqual(blocked["id"], 3)
        self.finish(process)
        self.assertEqual(self.forwarded(), [], "closed call must never reach the server")
        state = json.loads(self.state.read_text(encoding="utf-8"))
        self.assertEqual((state["blocked"], state["calls"]), (1, 0))
        self.assertEqual(state["client"], "claude-ai 1")
        self.assertIsNotNone(state["stopped_utc"])

    def test_switch_is_honoured_live_without_restart(self):
        process = self.start()
        self.request(process, 1, "initialize")
        self.set_open(True)
        self.assertEqual(self.request(process, 2, "tools/call", {"name": "get_scene_info"})["result"]["echo"], "tools/call")
        self.set_open(False)
        self.assertTrue(self.request(process, 3, "tools/call", {"name": "delete_object"})["result"]["isError"])
        self.set_open(True)
        self.assertEqual(self.request(process, 4, "tools/call", {"name": "list_skills"})["result"]["echo"], "tools/call")
        self.finish(process)
        self.assertEqual(self.forwarded(), ["get_scene_info", "list_skills"])
        state = json.loads(self.state.read_text(encoding="utf-8"))
        self.assertEqual((state["calls"], state["blocked"], state["last_tool"]), (2, 1, "list_skills"))

    def test_unreadable_config_fails_closed(self):
        process = self.start()
        self.request(process, 1, "initialize")
        self.config.write_text("{not json", encoding="utf-8")
        stamp = time.time_ns() + 2_000_000
        os.utime(self.config, ns=(stamp, stamp))
        self.assertTrue(self.request(process, 2, "tools/call", {"name": "get_scene_info"})["result"]["isError"])
        self.finish(process)
        self.assertEqual(self.forwarded(), [])

    def test_missing_server_reports_and_exits(self):
        self.set_open(True, server=self.root / "missing.cmd")
        process = subprocess.run([sys.executable, str(GATEWAY), "--config", str(self.config)],
                                 input=b"", capture_output=True, timeout=20)
        self.assertEqual(process.returncode, 2)
        self.assertIn(b"not found", process.stderr)


if __name__ == "__main__":
    unittest.main()
