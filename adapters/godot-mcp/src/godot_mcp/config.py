from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import json
import os

APP_DIR = Path(os.environ.get("APPDATA", Path.home())) / "ConstantineHub"
CONFIG_PATH = Path(os.environ.get("CONSTANTINE_GODOT_MCP_CONFIG", APP_DIR / "godot-mcp.json"))

@dataclass(slots=True)
class GodotSettings:
    project_root: Path
    editor_host: str = "127.0.0.1"
    editor_port: int = 6262
    runtime_host: str = "127.0.0.1"
    runtime_port: int = 6263
    godot_executable: str | None = None
    tools_c_root: Path | None = None
    max_file_bytes: int = 2_000_000

def _default_tools_c_root() -> Path | None:
    env = os.environ.get("TOOLS_C_ROOT")
    if env:
        return Path(env).expanduser().resolve()
    candidate = Path(r"E:\MyCreations\Tools_C")
    return candidate if candidate.exists() else None

def load_settings() -> GodotSettings:
    if not CONFIG_PATH.exists():
        raise RuntimeError(f"Godot MCP config does not exist: {CONFIG_PATH}. Configure a project in Constantine Hub first.")
    data = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
    root = Path(data.get("project_root", "")).expanduser().resolve()
    if not root.exists() or not (root / "project.godot").exists():
        raise RuntimeError(f"Configured Godot project is invalid: {root}")
    tools_c = data.get("tools_c_root")
    return GodotSettings(
        project_root=root,
        editor_host=str(data.get("editor_host", "127.0.0.1")),
        editor_port=int(data.get("editor_port", 6262)),
        runtime_host=str(data.get("runtime_host", "127.0.0.1")),
        runtime_port=int(data.get("runtime_port", 6263)),
        godot_executable=data.get("godot_executable") or None,
        tools_c_root=Path(tools_c).expanduser().resolve() if tools_c else _default_tools_c_root(),
        max_file_bytes=int(data.get("max_file_bytes", 2_000_000)),
    )

def write_default_config(project_root: str | Path) -> Path:
    APP_DIR.mkdir(parents=True, exist_ok=True)
    root = Path(project_root).expanduser().resolve()
    payload = {
        "schema_version": 1,
        "project_root": str(root),
        "editor_host": "127.0.0.1",
        "editor_port": 6262,
        "runtime_host": "127.0.0.1",
        "runtime_port": 6263,
        "godot_executable": None,
        "tools_c_root": str(_default_tools_c_root() or ""),
        "max_file_bytes": 2_000_000,
    }
    CONFIG_PATH.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    return CONFIG_PATH
