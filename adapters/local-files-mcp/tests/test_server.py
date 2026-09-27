import importlib
import json
import sys
from pathlib import Path


def test_mcp_server_module_imports_with_valid_config(tmp_path: Path, monkeypatch) -> None:
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
    monkeypatch.setenv("CONSTANTINE_FILES_CONFIG", str(config))
    sys.modules.pop("local_files_mcp.server", None)
    module = importlib.import_module("local_files_mcp.server")
    assert module.mcp is not None
    assert module.files_server_info()["version"] == "0.1.0"
