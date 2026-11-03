from __future__ import annotations

from typing import Any
from mcp.server.mcpserver.exceptions import ToolError

from .bounded_server import BoundedMCPServer

from .bridge_client import BridgeEndpoint, BridgeError, JsonLineBridge
from .config import load_settings
from .project_service import ProjectService
from .skills_service import GodotSkillsService

mcp = BoundedMCPServer("ConHub. Godot MCP")
_settings = load_settings()
_project = ProjectService(_settings)
_skills = GodotSkillsService(_settings)
_editor = JsonLineBridge(BridgeEndpoint(_settings.editor_host, _settings.editor_port, "Godot editor"), project_root=_settings.project_root)
_runtime = JsonLineBridge(BridgeEndpoint(_settings.runtime_host, _settings.runtime_port, "Godot runtime"), project_root=_settings.project_root)

def _bridge(target: str) -> JsonLineBridge:
    if target == "editor": return _editor
    if target == "runtime": return _runtime
    raise ValueError("target must be 'editor' or 'runtime'")

async def _call(bridge: JsonLineBridge, op: str, **params: Any) -> Any:
    try:
        return await bridge.acall(op, **params)
    except BridgeError as exc:
        raise ToolError(str(exc)) from exc

@mcp.tool()
async def godot_status() -> dict:
    """Check Godot editor/runtime bridge connectivity plus project and canonical Tools_C context."""
    result: dict[str, Any] = {
        "project_root": str(_settings.project_root),
        "editor_bridge": {"endpoint": f"{_settings.editor_host}:{_settings.editor_port}", "connected": False},
        "runtime_bridge": {"endpoint": f"{_settings.runtime_host}:{_settings.runtime_port}", "connected": False},
        "skills": _skills.list_skills(),
    }
    for name, bridge in (("editor", _editor), ("runtime", _runtime)):
        try:
            result[name] = await _call(bridge, "status")
            result[name + "_bridge"]["connected"] = True
        except Exception as exc:
            result[name + "_error"] = str(exc)
    result["managed_run"] = _project.get_output(after=0, limit=1)["running"]
    return result

@mcp.tool()
async def godot_get_scene_tree(target: str = "editor", max_depth: int = 8, include_internal: bool = False, max_nodes: int = 256, node_path: str = "", after_path: str = "", scene_id: int = 0, name_filter: str = "", type_filter: str = "") -> dict:
    """Read a bounded scene-tree page. Pass next_after as after_path and scene_id with unchanged options for the next page. node_path selects a subtree; name_filter matches substrings and type_filter selects a Godot class. First unfiltered page retains the legacy root tree; nodes is flat on every page. depth_limited branches need a subtree query or greater max_depth."""
    return await _call(_bridge(target), "scene_tree", max_depth=max(0, min(max_depth, 32)), include_internal=include_internal, max_nodes=max(1, min(max_nodes, 1024)), node_path=node_path, after_path=after_path, scene_id=scene_id, name_filter=name_filter, type_filter=type_filter)

@mcp.tool()
async def godot_inspect_node(target: str = "editor", node_path: str = "", property_filter: str | None = None) -> dict:
    """Inspect one node including class, path, groups, script and serialized properties."""
    if not node_path: raise ValueError("node_path is required")
    return await _call(_bridge(target), "inspect_node", node_path=node_path, property_filter=property_filter)

@mcp.tool()
async def godot_create_node(parent_path: str, node_type: str, target: str = "editor", node_name: str | None = None, properties: dict[str, Any] | None = None) -> dict:
    """Create a node under a parent in the edited scene or running game."""
    return await _call(_bridge(target), "create_node", parent_path=parent_path, node_type=node_type, node_name=node_name, properties=properties or {})

@mcp.tool()
async def godot_set_node_property(target: str = "editor", node_path: str = "", property: str = "", value: Any = None) -> dict:
    """Set one property on a node. Tagged JSON values support Godot vectors/colors/resources."""
    if not node_path or not property: raise ValueError("node_path and property are required")
    return await _call(_bridge(target), "set_property", node_path=node_path, property=property, value=value)

@mcp.tool()
async def godot_delete_node(target: str = "editor", node_path: str = "") -> dict:
    """Delete a node and its children. Inspect first; editor deletion is unsaved until save_scene."""
    if not node_path: raise ValueError("node_path is required")
    return await _call(_bridge(target), "delete_node", node_path=node_path)

@mcp.tool()
async def godot_attach_script(target: str = "editor", node_path: str = "", script_path: str = "") -> dict:
    """Attach an existing res:// script resource to a node."""
    if not script_path.startswith("res://"): raise ValueError("script_path must begin with res://")
    return await _call(_bridge(target), "attach_script", node_path=node_path, script_path=script_path)

@mcp.tool()
async def godot_save_scene(path: str | None = None) -> dict:
    """Save the current edited scene, optionally to a new res:// path."""
    if path is not None and not path.startswith("res://"): raise ValueError("path must begin with res://")
    return await _call(_editor, "save_scene", path=path)

@mcp.tool()
async def godot_open_scene(path: str) -> dict:
    """Open an existing .tscn or .scn file in the Godot editor."""
    if not path.startswith("res://"): raise ValueError("path must begin with res://")
    return await _call(_editor, "open_scene", path=path)

@mcp.tool()
def godot_list_project_files(path: str = "res://", pattern: str | None = None, recursive: bool = True, max_results: int = 500) -> dict:
    """List files below a res:// directory; hidden Godot caches are excluded."""
    return _project.list_files(path, pattern=pattern, recursive=recursive, max_results=max_results)

@mcp.tool()
def godot_read_project_file(path: str, max_bytes: int = 500_000) -> dict:
    """Read a UTF-8 text file inside res://. Binary and oversized files are rejected."""
    return _project.read_text(path, max_bytes=max_bytes)

@mcp.tool()
def godot_write_project_file(path: str, content: str, overwrite: bool = False) -> dict:
    """Create or overwrite an allowed UTF-8 project file under res://."""
    return _project.write_text(path, content, overwrite=overwrite)

@mcp.tool()
def godot_run_project(scene_path: str | None = None) -> dict:
    """Run the current Godot project as a managed child process and capture output."""
    if scene_path is not None and not scene_path.startswith("res://"): raise ValueError("scene_path must begin with res://")
    return _project.run_project(scene_path)

@mcp.tool()
def godot_stop_project() -> dict:
    """Stop the game process previously started by godot_run_project."""
    return _project.stop_project()

@mcp.tool()
def godot_get_output(after: int = 0, limit: int = 300) -> dict:
    """Read captured stdout and stderr from the managed Godot process."""
    return _project.get_output(after=after, limit=limit)

@mcp.tool()
def godot_get_errors(after: int = 0, limit: int = 200) -> dict:
    """Read error-classified stderr and engine/script error lines."""
    return _project.get_output(after=after, limit=limit, errors_only=True)

@mcp.tool()
async def godot_capture_game() -> dict:
    """Capture the running game's root viewport as a base64 PNG."""
    return await _call(_runtime, "capture_game")

@mcp.tool()
async def godot_send_input_action(action: str, pressed: bool = True, strength: float = 1.0, duration_ms: int = 0) -> dict:
    """Press/release a named Input Map action in the running game."""
    return await _call(_runtime, "input_action", action=action, pressed=pressed, strength=max(0.0, min(float(strength), 1.0)), duration_ms=max(0, min(int(duration_ms), 10_000)))

@mcp.tool()
async def godot_call_node_method(node_path: str, method: str, target: str = "runtime", arguments: list[Any] | None = None) -> dict:
    """Call a public method on a node; private and lifecycle-dangerous methods are blocked."""
    if method.startswith("_"): raise ValueError("Private methods are blocked")
    blocked = {"free", "queue_free", "notification", "set_script", "remove_child"}
    if method in blocked: raise ValueError(f"Dangerous lifecycle method is blocked: {method}")
    args = arguments or []
    if len(args) > 16: raise ValueError("At most 16 arguments are allowed")
    return await _call(_bridge(target), "call_method", node_path=node_path, method=method, arguments=args)

@mcp.tool()
def godot_git_status() -> dict:
    """Read Git branch and worktree status for the Godot project."""
    return _project.git_status()

@mcp.tool()
def godot_git_diff(staged: bool = False, paths: list[str] | None = None, stat: bool = False, max_chars: int = 120_000) -> dict:
    """Read an unstaged or staged Git diff restricted to the configured project."""
    return _project.git_diff(staged=staged, paths=paths, stat=stat, max_chars=max_chars)

@mcp.tool()
def godot_git_log(limit: int = 20, path: str | None = None) -> dict:
    """Read recent Git commits for the Godot project."""
    return _project.git_log(limit=limit, path=path)

@mcp.tool()
def godot_git_commit(message: str, paths: list[str]) -> dict:
    """Stage and commit only explicitly listed project paths."""
    return _project.git_commit(message, paths)

@mcp.tool()
def godot_git_push(remote: str = "origin", branch: str | None = None, set_upstream: bool = False) -> dict:
    """Push the current or specified branch using the machine's existing Git credentials."""
    return _project.git_push(remote=remote, branch=branch, set_upstream=set_upstream)

@mcp.tool()
async def godot_extended_catalogue() -> dict:
    """List native operations exposed by the ConHub. Godot editor bridge."""
    if not await _editor.aprobe(): return {"connected": False, "tools": []}
    return await _call(_editor, "catalogue")

@mcp.tool()
async def godot_extended_call(tool_name: str, parameters: dict[str, Any] | None = None) -> dict:
    """Fallback dispatcher for a safe native Godot bridge operation."""
    return await _call(_editor, "extended_call", tool_name=tool_name, parameters=parameters or {})

@mcp.tool()
def godot_list_skills() -> dict:
    """List canonical Tools_C Godot skills and verify the shared foundation contract."""
    return _skills.list_skills()

@mcp.tool()
def godot_read_skill(name: str) -> dict:
    """Read a canonical Tools_C Godot skill together with its foundation contract."""
    return _skills.read_skill(name)

@mcp.tool()
def godot_get_skill_context(task: str) -> dict:
    """Load canonical Tools_C Godot procedures relevant to a task before editing or QA."""
    return _skills.context_for_task(task)

def main() -> None:
    mcp.run()

if __name__ == "__main__":
    main()
