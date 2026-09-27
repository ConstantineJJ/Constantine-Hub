from __future__ import annotations

import asyncio
import json
import os
from pathlib import Path
import sys
import tempfile

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

EXPECTED_TOOLS = {
    "godot_status", "godot_get_scene_tree", "godot_inspect_node", "godot_create_node",
    "godot_set_node_property", "godot_delete_node", "godot_attach_script", "godot_save_scene",
    "godot_open_scene", "godot_list_project_files", "godot_read_project_file", "godot_write_project_file",
    "godot_run_project", "godot_stop_project", "godot_get_output", "godot_get_errors",
    "godot_capture_game", "godot_send_input_action", "godot_call_node_method", "godot_git_status",
    "godot_git_diff", "godot_git_log", "godot_git_commit", "godot_git_push",
    "godot_extended_catalogue", "godot_extended_call", "godot_list_skills", "godot_read_skill",
    "godot_get_skill_context",
}

def make_fixture(root: Path) -> tuple[Path, dict[str, str]]:
    project = root / "project"
    tools_c = root / "Tools_C"
    project.mkdir(parents=True)
    (project / "project.godot").write_text('[application]\nconfig/name="CI Godot Fixture"\n', encoding="utf-8")
    (project / "hello.txt").write_text("hello godot mcp\n", encoding="utf-8")
    (tools_c / "docs").mkdir(parents=True)
    (tools_c / "docs" / "foundation.md").write_text("# foundation\nCI contract\n", encoding="utf-8")
    for name in ("godot-project", "godot-asset-integration", "verification"):
        skill_dir = tools_c / "skills" / name
        skill_dir.mkdir(parents=True)
        (skill_dir / "SKILL.md").write_text(f"# {name}\nCI skill\n", encoding="utf-8")
    config = root / "godot-mcp.json"
    config.write_text(json.dumps({
        "schema_version": 1, "project_root": str(project),
        "editor_host": "127.0.0.1", "editor_port": 6262,
        "runtime_host": "127.0.0.1", "runtime_port": 6263,
        "godot_executable": None, "tools_c_root": str(tools_c), "max_file_bytes": 2000000,
    }), encoding="utf-8")
    env = os.environ.copy()
    env["CONSTANTINE_GODOT_MCP_CONFIG"] = str(config)
    env["TOOLS_C_ROOT"] = str(tools_c)
    return project, env

async def main() -> None:
    with tempfile.TemporaryDirectory(prefix="constantine-godot-ci-") as tmp:
        project, env = make_fixture(Path(tmp))
        params = StdioServerParameters(command=sys.executable, args=["-m", "godot_mcp.server"], env=env)
        async with stdio_client(params) as (read_stream, write_stream):
            async with ClientSession(read_stream, write_stream) as session:
                await session.initialize()
                tools = await session.list_tools()
                names = {tool.name for tool in tools.tools}
                missing = sorted(EXPECTED_TOOLS - names)
                assert not missing, f"Missing MCP tools: {missing}"
                skill_result = await session.call_tool("godot_list_skills", {})
                assert not skill_result.is_error, skill_result
                list_result = await session.call_tool("godot_list_project_files", {"path": "res://", "recursive": False})
                assert not list_result.is_error, list_result

        from godot_mcp.config import GodotSettings
        from godot_mcp.project_service import ProjectService
        service = ProjectService(GodotSettings(project_root=project))
        try:
            service.res_to_path("res://../escape.txt")
        except ValueError:
            pass
        else:
            raise AssertionError("res:// path traversal was not blocked")
        print(f"PASS: {len(EXPECTED_TOOLS)} Godot MCP tools registered; stdio initialize/list/call succeeded.")

if __name__ == "__main__":
    asyncio.run(main())
