from __future__ import annotations

from typing import Any
from mcp.server import MCPServer

from .bridge_client import BridgeEndpoint, JsonLineBridge
from .config import load_settings
from .project_service import ProjectService
from .skills_service import GodotSkillsService

mcp = MCPServer("Constantine Godot MCP")
_settings = load_settings()
_project = ProjectService(_settings)
_skills = GodotSkillsService(_settings)
_editor = JsonLineBridge(BridgeEndpoint(_settings.editor_host, _settings.editor_port, "Godot editor"))
_runtime = JsonLineBridge(BridgeEndpoint(_settings.runtime_host, _settings.runtime_port, "Godot runtime"))

def _bridge(target: str) -> JsonLineBridge:
    if target == "editor": return _editor
    if target == "runtime": return _runtime
    raise ValueError("target must be 'editor' or 'runtime'")

@mcp.tool()
def godot_status() -> dict:
    """Check Godot editor/runtime bridge connectivity plus project and canonical Tools_C context."""
    result: dict[str, Any] = {
        "project_root": str(_settings.project_root),
        "editor_bridge": {"endpoint": f"{_settings.editor_host}:{_settings.editor_port}", "connected": _editor.probe()},
        "runtime_bridge": {"endpoint": f"{_settings.runtime_host}:{_settings.runtime_port}", "connected": _runtime.probe()},
        "skills": _skills.list_skills(),
    }
    if result["editor_bridge"]["connected"]:
        try: result["editor"] = _editor.call("status")
        except Exception as exc: result["editor_error"] = str(exc)
    if result["runtime_bridge"]["connected"]:
        try: result["runtime"] = _runtime.call("status")
        except Exception as exc: result["runtime_error"] = str(exc)
    result["managed_run"] = _project.get_output(after=0, limit=1)["running"]
    return result

@mcp.tool()
def godot_get_scene_tree(target: str = "editor", max_depth: int = 8, include_internal: bool = False) -> dict:
    """Read the current edited scene tree or running game's scene tree."""
    return _bridge(target).call("scene_tree", max_depth=max_depth, include_internal=include_internal)

@mcp.tool()
def godot_inspect_node(target: str = "editor", node_path: str = "", property_filter: str | None = None) -> dict:
    """Inspect one node including class, path, groups, script and serialized properties."""
    if not node_path: raise ValueError("node_path is required")
    return _bridge(target).call("inspect_node", node_path=node_path, property_filter=property_filter)

@mcp.tool()
def godot_create_node(parent_path: str, node_type: str, target: str = "editor", node_name: str | None = None, properties: dict[str, Any] | None = None) -> dict:
    """Create a node under a parent in the edited scene or running game."""
    return _bridge(target).call("create_node", parent_path=parent_path, node_type=node_type, node_name=node_name, properties=properties or {})

@mcp.tool()
def godot_set_node_property(target: str = "editor", node_path: str = "", property: str = "", value: Any = None) -> dict:
    """Set one property on a node. Tagged JSON values support Godot vectors/colors/resources."""
    if not node_path or not property: raise ValueError("node_path and property are required")
    return _bridge(target).call("set_property", node_path=node_path, property=property, value=value)

@mcp.tool()
def godot_delete_node(target: str = "editor", node_path: str = "") -> dict:
    """Delete a node and its children. Inspect first; editor deletion is unsaved until save_scene."""
    if not node_path: raise ValueError("node_path is required")
    return _bridge(target).call("delete_node", node_path=node_path)

@mcp.tool()
def godot_attach_script(target: str = "editor", node_path: str = "", script_path: str = "") -> dict:
    """Attach an existing res:// script resource to a node."""
    if not script_path.startswith("res://"): raise ValueError("script_path must begin with res://")
    return _bridge(target).call("attach_script", node_path=node_path, script_path=script_path)

@mcp.tool()
def godot_save_scene(path: str | None = None) -> dict:
    """Save the current edited scene, optionally to a new res:// path."""
    if path is not None and not path.startswith("res://"): raise ValueError("path must begin with res://")
    return _editor.call("save_scene", path=path)

@mcp.tool()
def godot_open_scene(path: str) -> dict:
    """Open an existing .tscn or .scn file in the Godot editor."""
    if not path.startswith("res://"): raise ValueError("path must begin with res://")
    return _editor.call("open_scene", path=path)

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
def godot_capture_game() -> dict:
    """Capture the running game's root viewport as a base64 PNG."""
    return _runtime.call("capture_game")

@mcp.tool()
def godot_send_input_action(action: str, pressed: bool = True, strength: float = 1.0, duration_ms: int = 0) -> dict:
    """Press/release a named Input Map action in the running game."""
    return _runtime.call("input_action", action=action, pressed=pressed, strength=max(0.0, min(float(strength), 1.0)), duration_ms=max(0, min(int(duration_ms), 10_000)))

@mcp.tool()
def godot_call_node_method(node_path: str, method: str, target: str = "runtime", arguments: list[Any] | None = None) -> dict:
    """Call a public method on a node; private and lifecycle-dangerous methods are blocked."""
    if method.startswith("_"): raise ValueError("Private methods are blocked")
    blocked = {"free", "queue_free", "notification", "set_script", "remove_child"}
    if method in blocked: raise ValueError(f"Dangerous lifecycle method is blocked: {method}")
    args = arguments or []
    if len(args) > 16: raise ValueError("At most 16 arguments are allowed")
    return _bridge(target).call("call_method", node_path=node_path, method=method, arguments=args)

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
def godot_extended_catalogue() -> dict:
    """List native operations exposed by the Constantine Godot editor bridge."""
    if not _editor.probe(): return {"connected": False, "tools": []}
    return _editor.call("catalogue")

@mcp.tool()
def godot_extended_call(tool_name: str, parameters: dict[str, Any] | None = None) -> dict:
    """Fallback dispatcher for a safe native Godot bridge operation."""
    return _editor.call("extended_call", tool_name=tool_name, parameters=parameters or {})

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
