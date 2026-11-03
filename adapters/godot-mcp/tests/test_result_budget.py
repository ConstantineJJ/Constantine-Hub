import asyncio
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from godot_mcp.bounded_server import BoundedMCPServer, MAX_MCP_RESULT_BYTES


class ResultBudgetTests(unittest.TestCase):
    def test_wire_budget_covers_text_structured_and_json_escaping(self):
        async def run():
            server = BoundedMCPServer("fixture")

            @server.tool()
            def giant() -> dict:
                return {"value": '\x00"\\' * 300_000}

            @server.tool()
            def healthy() -> dict:
                return {"connected": True}

            result = await server.call_tool("giant", {})
            self.assertTrue(result.is_error)
            wire = result.model_dump_json(by_alias=True).encode()
            self.assertLess(len(wire), MAX_MCP_RESULT_BYTES)
            self.assertIn("budget", result.content[0].text)
            after = await server.call_tool("healthy", {})
            self.assertFalse(after.is_error)
            # Even escaping the entire MCP result inside a control-plane JSON
            # string leaves several MiB of margin below the 10 MiB limit.
            self.assertLess(len(json.dumps(after.model_dump_json(by_alias=True)).encode()), 10 * 1024 * 1024)
        asyncio.run(run())


if __name__ == "__main__":
    unittest.main()
