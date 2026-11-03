"""Final wire-size gate, including MCP's duplicated text/structured content."""
import json
import logging

from mcp.server import MCPServer
from mcp.types import CallToolResult, TextContent

MAX_MCP_RESULT_BYTES = 1024 * 1024
_logger = logging.getLogger(__name__)


def _bounded(result, tool_name: str):
    total = 0
    # Count the actual MCP JSON representation, not the unescaped source data.
    # Even worst-case JSON-in-JSON escaping has headroom below 10 MiB.
    for chunk in json.JSONEncoder(ensure_ascii=False, separators=(",", ":")).iterencode(
        result.model_dump(mode="json", by_alias=True, exclude_none=True)
    ):
        total += len(chunk.encode("utf-8"))
        if total > MAX_MCP_RESULT_BYTES:
            _logger.warning("Tool %s response rejected: serialized MCP result exceeds %d bytes", tool_name, MAX_MCP_RESULT_BYTES)
            return CallToolResult(content=[TextContent(type="text", text=(
                "Response exceeds the safe serialized MCP budget (1 MiB). "
                "Use godot_get_scene_tree with max_nodes/node_path/after_path, "
                "a narrower property_filter, or smaller file/output pages. "
                "The connection remains available."
            ))], is_error=True)
    return result


class BoundedMCPServer(MCPServer):
    async def call_tool(self, name, arguments, context=None):
        return _bounded(await super().call_tool(name, arguments, context), name)

    async def _handle_call_tool(self, ctx, params):
        # Also cover the SDK's generated tool-error responses.
        return _bounded(await super()._handle_call_tool(ctx, params), params.name)
