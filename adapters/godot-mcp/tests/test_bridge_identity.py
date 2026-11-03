"""A selected project must never dispatch an edit to a different open editor."""
from __future__ import annotations

import json
from pathlib import Path
import socketserver
import sys
import tempfile
import threading
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from godot_mcp.bridge_client import BridgeEndpoint, BridgeError, JsonLineBridge


class BridgeIdentityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.expected = Path(self.temp.name) / "selected"
        self.other = Path(self.temp.name) / "other"
        self.expected.mkdir()
        self.other.mkdir()
        self.requests = []
        owner = self

        class Handler(socketserver.StreamRequestHandler):
            def handle(self):
                raw = self.rfile.readline()
                if not raw:
                    return
                request = json.loads(raw)
                owner.requests.append(request)
                result = {"project_path": str(owner.reported)} if request["op"] == "status" else {"edited": True}
                self.wfile.write((json.dumps({"id": request["id"], "ok": True, "result": result}) + "\n").encode())

        self.server = socketserver.TCPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.endpoint = BridgeEndpoint("127.0.0.1", self.server.server_address[1], "test")
        self.reported = self.other

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()
        self.temp.cleanup()

    def test_wrong_project_edit_is_blocked_before_dispatch(self):
        bridge = JsonLineBridge(self.endpoint, project_root=self.expected)
        with self.assertRaisesRegex(BridgeError, "project"):
            bridge.call("create_node", node_type="Node3D")
        self.assertEqual([r["op"] for r in self.requests], ["status"])
        self.assertFalse(bridge.probe())

    def test_matching_project_dispatch_carries_identity(self):
        self.reported = self.expected
        bridge = JsonLineBridge(self.endpoint, project_root=self.expected)
        self.assertEqual(bridge.call("create_node", node_type="Node3D"), {"edited": True})
        self.assertEqual([r["op"] for r in self.requests], ["status", "create_node"])
        self.assertTrue(all(r["project_root"] == str(self.expected) for r in self.requests))
        self.assertTrue(bridge.probe())


if __name__ == "__main__":
    unittest.main()
