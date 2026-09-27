import asyncio
import json
import sys
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


def test_stdio_initialize_and_list_tools(tmp_path: Path) -> None:
    root = tmp_path / "allowed"
    root.mkdir()
    config = tmp_path / "config.json"
    config.write_text(
        json.dumps(
            {
                "schema_version": 1,
                "allowed_roots": [
                    {
                        "path": str(root),
                        "permissions": ["read", "write", "delete"],
                    }
                ],
                "audit_log": str(tmp_path / "audit.jsonl"),
            }
        ),
        encoding="utf-8",
    )

    async def run() -> None:
        params = StdioServerParameters(
            command=sys.executable,
            args=["-m", "local_files_mcp.server"],
            env={"CONSTANTINE_FILES_CONFIG": str(config)},
        )
        async with stdio_client(params) as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                result = await session.list_tools()
                names = {tool.name for tool in result.tools}
                assert "files_server_info" in names
                assert "files_roots" in names
                assert "files_write_text" in names
                assert "files_delete" in names
                assert len(names) >= 14

    asyncio.run(run())
