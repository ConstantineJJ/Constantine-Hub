"""Request failures must stay local; later status calls use fresh sockets."""
from __future__ import annotations

import asyncio
import json
from pathlib import Path
import socketserver
import sys
import threading
import time
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from godot_mcp.bridge_client import BridgeEndpoint, BridgeError, JsonLineBridge


class TransportLimitsTests(unittest.TestCase):
    def setUp(self):
        owner = self
        self.mode = "healthy"
        self.started = threading.Event()
        self.disconnected = threading.Event()

        class Handler(socketserver.StreamRequestHandler):
            def handle(self):
                request = json.loads(self.rfile.readline())
                mode = owner.mode
                reply = {"id": request["id"], "ok": True, "result": {"healthy": True}}
                if mode == "missing_id":
                    reply.pop("id")
                elif mode == "wrong_id":
                    reply["id"] = "old-request"
                elif mode == "array":
                    reply = []
                elif mode == "oversize":
                    reply["result"] = "x" * 600_000
                elif mode == "closed":
                    return
                elif mode == "slow":
                    owner.started.set()
                    self.connection.settimeout(2)
                    if not self.connection.recv(1):
                        owner.disconnected.set()
                    return
                elif mode == "drip":
                    try:
                        for _ in range(100):
                            self.wfile.write(b" ")
                            self.wfile.flush()
                            time.sleep(0.02)
                    except OSError:
                        pass
                    return
                try:
                    self.wfile.write((json.dumps(reply) + "\n").encode())
                except OSError:
                    pass

        class Server(socketserver.ThreadingTCPServer):
            daemon_threads = True
            allow_reuse_address = True

        self.server = Server(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.bridge = JsonLineBridge(BridgeEndpoint("127.0.0.1", self.server.server_address[1], "fixture"), timeout=0.2)

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()

    def test_bad_frames_and_oversize_fail_without_poisoning_next_request(self):
        for mode in ("missing_id", "wrong_id", "array", "oversize", "closed"):
            with self.subTest(mode=mode):
                self.mode = mode
                with self.assertRaises(BridgeError):
                    self.bridge.call("scene_tree")
                self.mode = "healthy"
                self.assertEqual(self.bridge.call("status"), {"healthy": True})

    def test_dripping_response_is_bounded_by_total_deadline(self):
        self.mode = "drip"
        start = time.monotonic()
        with self.assertRaises(BridgeError):
            self.bridge.call("scene_tree")
        self.assertLess(time.monotonic() - start, 0.7)

    def test_cancelled_async_request_closes_socket_and_status_still_works(self):
        async def run():
            self.mode = "slow"
            task = asyncio.create_task(self.bridge.acall("scene_tree"))
            for _ in range(100):
                if self.started.is_set():
                    break
                await asyncio.sleep(0.005)
            self.assertTrue(self.started.is_set())
            task.cancel()
            with self.assertRaises(asyncio.CancelledError):
                await task
            self.mode = "healthy"
            self.assertEqual(await self.bridge.acall("status"), {"healthy": True})
        asyncio.run(run())
        self.assertTrue(self.disconnected.wait(1))


if __name__ == "__main__":
    unittest.main()
