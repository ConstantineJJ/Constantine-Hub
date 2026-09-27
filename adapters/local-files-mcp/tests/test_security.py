from pathlib import Path

import pytest

from local_files_mcp.config import Limits, RootRule, Settings
from local_files_mcp.security import AccessDenied, AccessGuard


def make_guard(tmp_path: Path) -> tuple[AccessGuard, Path, Path]:
    root = tmp_path / "allowed"
    other = tmp_path / "other"
    root.mkdir()
    other.mkdir()
    settings = Settings(
        config_path=tmp_path / "config.json",
        allowed_roots=(RootRule(root.resolve(), frozenset({"read", "write", "delete"})),),
        limits=Limits(),
        audit_log=tmp_path / "audit.jsonl",
    )
    return AccessGuard(settings), root, other


def test_allows_existing_path_inside_root(tmp_path: Path) -> None:
    guard, root, _ = make_guard(tmp_path)
    target = root / "hello.txt"
    target.write_text("hello", encoding="utf-8")
    assert guard.authorize_existing(str(target), "read").path == target.resolve()


def test_blocks_path_outside_root(tmp_path: Path) -> None:
    guard, _, other = make_guard(tmp_path)
    target = other / "note.txt"
    target.write_text("x", encoding="utf-8")
    with pytest.raises(AccessDenied):
        guard.authorize_existing(str(target), "read")


def test_blocks_parent_escape(tmp_path: Path) -> None:
    guard, root, other = make_guard(tmp_path)
    target = other / "note.txt"
    target.write_text("x", encoding="utf-8")
    attempted = root / ".." / "other" / "note.txt"
    with pytest.raises(AccessDenied):
        guard.authorize_existing(str(attempted), "read")


def test_refuses_destructive_operation_on_allowed_root(tmp_path: Path) -> None:
    guard, root, _ = make_guard(tmp_path)
    with pytest.raises(AccessDenied):
        guard.assert_not_root(root.resolve())
