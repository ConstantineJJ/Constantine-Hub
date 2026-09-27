from pathlib import Path

from local_files_mcp.config import Limits, RootRule, Settings
from local_files_mcp.service import LocalFilesService


def make_service(tmp_path: Path) -> tuple[LocalFilesService, Path]:
    root = tmp_path / "allowed"
    root.mkdir()
    settings = Settings(
        config_path=tmp_path / "config.json",
        allowed_roots=(RootRule(root.resolve(), frozenset({"read", "write", "delete"})),),
        limits=Limits(
            max_read_bytes=100_000,
            max_write_bytes=100_000,
            max_search_file_bytes=100_000,
            max_results=50,
            max_tree_entries=100,
        ),
        audit_log=tmp_path / "audit.jsonl",
    )
    return LocalFilesService(settings), root


def test_write_read_edit_move_copy_delete_round_trip(tmp_path: Path) -> None:
    service, root = make_service(tmp_path)
    source = root / "source.txt"

    created = service.write_text(str(source), "alpha\nbeta\n")
    assert created["bytes"] > 0
    assert service.read_text(str(source))["content"] == "alpha\nbeta\n"

    edited = service.edit_text(str(source), "beta", "gamma", backup=False)
    assert edited["replacements"] == 1
    assert service.read_text(str(source))["content"] == "alpha\ngamma\n"

    service.append_text(str(source), "delta\n")
    assert "delta" in service.read_text(str(source))["content"]

    copied = root / "copied.txt"
    service.copy(str(source), str(copied))
    assert copied.read_text(encoding="utf-8") == source.read_text(encoding="utf-8")

    moved = root / "moved.txt"
    service.move(str(copied), str(moved))
    assert moved.exists()
    assert not copied.exists()

    service.delete(str(moved))
    assert not moved.exists()
    assert (tmp_path / "audit.jsonl").exists()


def test_name_and_text_search(tmp_path: Path) -> None:
    service, root = make_service(tmp_path)
    nested = root / "nested"
    nested.mkdir()
    (nested / "ProjectPulse.md").write_text("Bridge status: ready\n", encoding="utf-8")

    names = service.search_names(str(root), "pulse")
    assert any(item["name"] == "ProjectPulse.md" for item in names["results"])

    matches = service.search_text(str(root), "bridge status", file_glob="*.md")
    assert matches["results"][0]["line"] == 1


def test_recursive_delete_requires_explicit_flag(tmp_path: Path) -> None:
    service, root = make_service(tmp_path)
    directory = root / "tree"
    directory.mkdir()
    (directory / "file.txt").write_text("x", encoding="utf-8")

    try:
        service.delete(str(directory), recursive=False)
    except OSError:
        pass
    else:
        raise AssertionError("non-empty directory deletion should require recursive=True")

    service.delete(str(directory), recursive=True)
    assert not directory.exists()
