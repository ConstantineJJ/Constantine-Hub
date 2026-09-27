from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any

PERMISSIONS = frozenset({"read", "write", "delete"})


class ConfigError(RuntimeError):
    """Raised when Local Files MCP configuration is missing or invalid."""


@dataclass(frozen=True)
class RootRule:
    path: Path
    permissions: frozenset[str]


@dataclass(frozen=True)
class Limits:
    max_read_bytes: int = 2_000_000
    max_write_bytes: int = 2_000_000
    max_search_file_bytes: int = 1_000_000
    max_results: int = 200
    max_tree_entries: int = 2_000


@dataclass(frozen=True)
class Settings:
    config_path: Path
    allowed_roots: tuple[RootRule, ...]
    limits: Limits
    audit_log: Path


def default_config_path() -> Path:
    explicit = os.environ.get("CONSTANTINE_FILES_CONFIG")
    if explicit:
        return Path(os.path.expandvars(explicit)).expanduser()

    appdata = os.environ.get("APPDATA")
    base = Path(appdata) if appdata else Path.home() / ".config"
    return base / "ConstantineHub" / "local-files-mcp.json"


def _positive_int(value: Any, name: str, default: int) -> int:
    if value is None:
        return default
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise ConfigError(f"{name} must be a positive integer")
    return value


def _load_root(item: Any, index: int) -> RootRule:
    if not isinstance(item, dict):
        raise ConfigError(f"allowed_roots[{index}] must be an object")

    raw_path = item.get("path")
    if not isinstance(raw_path, str) or not raw_path.strip():
        raise ConfigError(f"allowed_roots[{index}].path must be a non-empty string")

    expanded = Path(os.path.expandvars(raw_path)).expanduser()
    if not expanded.is_absolute():
        raise ConfigError(f"allowed_roots[{index}].path must be absolute: {raw_path}")
    if not expanded.exists() or not expanded.is_dir():
        raise ConfigError(f"allowed root does not exist or is not a directory: {expanded}")

    try:
        resolved = expanded.resolve(strict=True)
    except OSError as exc:
        raise ConfigError(f"cannot resolve allowed root {expanded}: {exc}") from exc

    raw_permissions = item.get("permissions", ["read"])
    if not isinstance(raw_permissions, list) or not raw_permissions:
        raise ConfigError(f"allowed_roots[{index}].permissions must be a non-empty array")
    if not all(isinstance(permission, str) for permission in raw_permissions):
        raise ConfigError(f"allowed_roots[{index}].permissions must contain strings")

    permissions = frozenset(raw_permissions)
    unknown = permissions - PERMISSIONS
    if unknown:
        raise ConfigError(
            f"allowed_roots[{index}] has unsupported permissions: {sorted(unknown)}"
        )

    return RootRule(path=resolved, permissions=permissions)


def load_settings(config_path: Path | None = None) -> Settings:
    path = (config_path or default_config_path()).expanduser()
    if not path.exists():
        raise ConfigError(
            f"Local Files MCP config not found: {path}. Run setup.ps1 or copy config.example.json."
        )

    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ConfigError(f"cannot read config {path}: {exc}") from exc

    if not isinstance(data, dict):
        raise ConfigError("config root must be a JSON object")
    if data.get("schema_version") != 1:
        raise ConfigError("schema_version must be 1")

    raw_roots = data.get("allowed_roots")
    if not isinstance(raw_roots, list) or not raw_roots:
        raise ConfigError("allowed_roots must be a non-empty array")

    roots = tuple(_load_root(item, index) for index, item in enumerate(raw_roots))
    normalized = [os.path.normcase(str(root.path)) for root in roots]
    if len(normalized) != len(set(normalized)):
        raise ConfigError("allowed_roots contains duplicate canonical paths")

    raw_limits = data.get("limits", {})
    if not isinstance(raw_limits, dict):
        raise ConfigError("limits must be an object")

    defaults = Limits()
    limits = Limits(
        max_read_bytes=_positive_int(
            raw_limits.get("max_read_bytes"), "limits.max_read_bytes", defaults.max_read_bytes
        ),
        max_write_bytes=_positive_int(
            raw_limits.get("max_write_bytes"), "limits.max_write_bytes", defaults.max_write_bytes
        ),
        max_search_file_bytes=_positive_int(
            raw_limits.get("max_search_file_bytes"),
            "limits.max_search_file_bytes",
            defaults.max_search_file_bytes,
        ),
        max_results=_positive_int(
            raw_limits.get("max_results"), "limits.max_results", defaults.max_results
        ),
        max_tree_entries=_positive_int(
            raw_limits.get("max_tree_entries"),
            "limits.max_tree_entries",
            defaults.max_tree_entries,
        ),
    )

    raw_audit = data.get("audit_log")
    if raw_audit is None:
        audit_log = path.parent / "local-files-audit.jsonl"
    elif isinstance(raw_audit, str) and raw_audit.strip():
        audit_log = Path(os.path.expandvars(raw_audit)).expanduser()
        if not audit_log.is_absolute():
            audit_log = path.parent / audit_log
    else:
        raise ConfigError("audit_log must be a non-empty string when supplied")

    return Settings(
        config_path=path.resolve(strict=True),
        allowed_roots=roots,
        limits=limits,
        audit_log=audit_log,
    )
