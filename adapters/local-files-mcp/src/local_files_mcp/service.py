from __future__ import annotations

import fnmatch
import os
import shutil
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterator

from .audit import AuditLogger
from .config import Settings
from .security import AccessDenied, AccessGuard, is_reparse_point


class LocalFilesService:
    def __init__(self, settings: Settings):
        self.settings = settings
        self.guard = AccessGuard(settings)
        self.audit = AuditLogger(settings.audit_log)

    @staticmethod
    def _iso_mtime(timestamp: float) -> str:
        return datetime.fromtimestamp(timestamp, timezone.utc).isoformat()

    @staticmethod
    def _size_utf8(text: str) -> int:
        return len(text.encode("utf-8"))

    def _bounded_results(self, requested: int | None) -> int:
        if requested is None:
            return self.settings.limits.max_results
        if requested <= 0:
            raise ValueError("max_results must be positive")
        return min(requested, self.settings.limits.max_results)

    def _entry_dict(self, path: Path) -> dict[str, Any]:
        info = path.lstat()
        reparse = is_reparse_point(path)
        if reparse:
            kind = "reparse"
        elif path.is_dir():
            kind = "directory"
        elif path.is_file():
            kind = "file"
        else:
            kind = "other"
        return {
            "path": str(path),
            "name": path.name,
            "type": kind,
            "size": info.st_size if kind == "file" else None,
            "mtime_utc": self._iso_mtime(info.st_mtime),
            "reparse_point": reparse,
        }

    def _walk_safe(self, root: Path, max_depth: int) -> Iterator[tuple[Path, int]]:
        stack: list[tuple[Path, int]] = [(root, 0)]
        while stack:
            directory, depth = stack.pop()
            try:
                entries = sorted(directory.iterdir(), key=lambda item: item.name.casefold())
            except OSError:
                continue
            child_dirs: list[Path] = []
            for entry in entries:
                yield entry, depth + 1
                if depth < max_depth and entry.is_dir() and not is_reparse_point(entry):
                    child_dirs.append(entry)
            for child in reversed(child_dirs):
                stack.append((child, depth + 1))

    def _assert_no_reparse_tree(self, root: Path) -> None:
        if is_reparse_point(root):
            raise AccessDenied(f"copy/move of reparse point is blocked: {root}")
        if root.is_dir():
            for entry, _ in self._walk_safe(root, max_depth=1000):
                if is_reparse_point(entry):
                    raise AccessDenied(
                        f"directory tree contains a symlink/junction/reparse point: {entry}"
                    )

    def _ensure_parent_for_target(self, target: Path, create_parents: bool) -> None:
        parent = target.parent
        if not parent.exists():
            if not create_parents:
                raise FileNotFoundError(f"parent directory does not exist: {parent}")
            # The unresolved target was already allowlist-checked. Create parents,
            # then strictly re-check the final parent so a junction cannot redirect it.
            parent.mkdir(parents=True, exist_ok=True)
        authorized_parent = self.guard.ensure_parent(target, "write")
        if not authorized_parent.path.is_dir():
            raise NotADirectoryError(str(authorized_parent.path))

    def _atomic_write(self, path: Path, text: str) -> None:
        data = text.encode("utf-8")
        if len(data) > self.settings.limits.max_write_bytes:
            raise ValueError(
                f"write exceeds max_write_bytes={self.settings.limits.max_write_bytes}"
            )
        fd, temp_name = tempfile.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=path.parent)
        temp_path = Path(temp_name)
        try:
            with os.fdopen(fd, "wb") as handle:
                handle.write(data)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temp_path, path)
        finally:
            try:
                if temp_path.exists():
                    temp_path.unlink()
            except OSError:
                pass

    @staticmethod
    def _backup_name(path: Path) -> Path:
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
        return path.with_name(f"{path.name}.bak-{stamp}")

    def _audit_error(self, operation: str, paths: list[str], exc: Exception) -> None:
        try:
            self.audit.write(
                operation,
                status="error",
                paths=paths,
                details={"error": type(exc).__name__, "message": str(exc)[:1000]},
            )
        except OSError:
            pass

    def server_info(self) -> dict[str, Any]:
        return {
            "name": "Constantine Local Files MCP",
            "version": "0.1.0",
            "config_path": str(self.settings.config_path),
            "audit_log": str(self.settings.audit_log),
            "capabilities": [
                "read",
                "search",
                "create",
                "write",
                "edit",
                "append",
                "copy",
                "move",
                "delete",
            ],
            "shell_execution": False,
        }

    def roots(self) -> dict[str, Any]:
        return {
            "roots": [
                {"path": str(rule.path), "permissions": sorted(rule.permissions)}
                for rule in self.settings.allowed_roots
            ]
        }

    def exists(self, path: str) -> dict[str, Any]:
        target = self.guard.authorize_target(path, "read").path
        return {"path": str(target), "exists": target.exists()}

    def stat(self, path: str) -> dict[str, Any]:
        target = self.guard.authorize_existing(path, "read").path
        result = self._entry_dict(target)
        result["canonical_path"] = str(target)
        return result

    def list_directory(
        self,
        path: str,
        *,
        recursive: bool = False,
        max_depth: int = 2,
        pattern: str = "*",
    ) -> dict[str, Any]:
        directory = self.guard.authorize_existing(path, "read").path
        if not directory.is_dir():
            raise NotADirectoryError(str(directory))
        if max_depth < 0 or max_depth > 32:
            raise ValueError("max_depth must be between 0 and 32")
        if not pattern:
            pattern = "*"

        entries: list[dict[str, Any]] = []
        limit = self.settings.limits.max_tree_entries
        if recursive:
            iterator = self._walk_safe(directory, max_depth=max_depth)
        else:
            iterator = ((entry, 1) for entry in sorted(directory.iterdir(), key=lambda p: p.name.casefold()))

        truncated = False
        for entry, depth in iterator:
            if not fnmatch.fnmatch(entry.name, pattern):
                continue
            item = self._entry_dict(entry)
            item["depth"] = depth
            entries.append(item)
            if len(entries) >= limit:
                truncated = True
                break

        return {
            "path": str(directory),
            "recursive": recursive,
            "pattern": pattern,
            "entries": entries,
            "truncated": truncated,
            "limit": limit,
        }

    def read_text(
        self,
        path: str,
        *,
        start_line: int = 1,
        end_line: int | None = None,
    ) -> dict[str, Any]:
        target = self.guard.authorize_existing(path, "read").path
        if not target.is_file():
            raise IsADirectoryError(str(target))
        size = target.stat().st_size
        if size > self.settings.limits.max_read_bytes:
            raise ValueError(
                f"file is {size} bytes; max_read_bytes={self.settings.limits.max_read_bytes}"
            )
        if start_line < 1:
            raise ValueError("start_line must be >= 1")
        if end_line is not None and end_line < start_line:
            raise ValueError("end_line must be >= start_line")

        try:
            text = target.read_text(encoding="utf-8")
        except UnicodeDecodeError as exc:
            raise ValueError("v0.1 reads UTF-8 text files only") from exc

        lines = text.splitlines(keepends=True)
        start_index = start_line - 1
        end_index = end_line if end_line is not None else len(lines)
        selected = "".join(lines[start_index:end_index])
        actual_end = min(end_index, len(lines))
        return {
            "path": str(target),
            "start_line": start_line,
            "end_line": actual_end,
            "total_lines": len(lines),
            "size_bytes": size,
            "content": selected,
        }

    def search_names(
        self, root: str, query: str, *, max_results: int | None = None
    ) -> dict[str, Any]:
        directory = self.guard.authorize_existing(root, "read").path
        if not directory.is_dir():
            raise NotADirectoryError(str(directory))
        if not query:
            raise ValueError("query must not be empty")
        limit = self._bounded_results(max_results)
        needle = query.casefold()
        results: list[dict[str, Any]] = []
        for entry, _ in self._walk_safe(directory, max_depth=32):
            if needle in entry.name.casefold():
                results.append(self._entry_dict(entry))
                if len(results) >= limit:
                    break
        return {"root": str(directory), "query": query, "results": results, "limit": limit}

    def search_text(
        self,
        root: str,
        query: str,
        *,
        file_glob: str = "*",
        case_sensitive: bool = False,
        max_results: int | None = None,
    ) -> dict[str, Any]:
        directory = self.guard.authorize_existing(root, "read").path
        if not directory.is_dir():
            raise NotADirectoryError(str(directory))
        if not query:
            raise ValueError("query must not be empty")
        limit = self._bounded_results(max_results)
        needle = query if case_sensitive else query.casefold()
        results: list[dict[str, Any]] = []
        skipped_large = 0
        skipped_binary = 0

        for entry, _ in self._walk_safe(directory, max_depth=32):
            if len(results) >= limit:
                break
            if is_reparse_point(entry) or not entry.is_file():
                continue
            if not fnmatch.fnmatch(entry.name, file_glob):
                continue
            try:
                size = entry.stat().st_size
            except OSError:
                continue
            if size > self.settings.limits.max_search_file_bytes:
                skipped_large += 1
                continue
            try:
                text = entry.read_text(encoding="utf-8")
            except (UnicodeDecodeError, OSError):
                skipped_binary += 1
                continue
            for line_number, line in enumerate(text.splitlines(), start=1):
                haystack = line if case_sensitive else line.casefold()
                if needle in haystack:
                    results.append(
                        {
                            "path": str(entry),
                            "line": line_number,
                            "text": line[:500],
                        }
                    )
                    if len(results) >= limit:
                        break

        return {
            "root": str(directory),
            "query": query,
            "file_glob": file_glob,
            "results": results,
            "limit": limit,
            "skipped_large_files": skipped_large,
            "skipped_non_utf8_files": skipped_binary,
        }

    def create_directory(
        self, path: str, *, parents: bool = True, exist_ok: bool = False
    ) -> dict[str, Any]:
        operation = "create_directory"
        try:
            target = self.guard.authorize_target(path, "write").path
            if target.exists():
                if exist_ok and target.is_dir():
                    return {"path": str(target), "created": False}
                raise FileExistsError(str(target))
            if parents:
                target.mkdir(parents=True, exist_ok=False)
            else:
                self.guard.ensure_parent(target, "write")
                target.mkdir()
            canonical = target.resolve(strict=True)
            self.guard.authorize_existing(str(canonical), "write")
            self.audit.write(operation, status="ok", paths=[str(canonical)])
            return {"path": str(canonical), "created": True}
        except Exception as exc:
            self._audit_error(operation, [path], exc)
            raise

    def write_text(
        self,
        path: str,
        content: str,
        *,
        overwrite: bool = False,
        create_parents: bool = False,
        backup: bool = False,
    ) -> dict[str, Any]:
        operation = "write_text"
        backup_path: Path | None = None
        try:
            target = self.guard.authorize_target(path, "write").path
            existed = target.exists()
            if existed and target.is_dir():
                raise IsADirectoryError(str(target))
            if existed and not overwrite:
                raise FileExistsError(f"target exists; set overwrite=true: {target}")
            self._ensure_parent_for_target(target, create_parents)
            if existed:
                target = self.guard.authorize_existing(str(target), "write").path
            if backup and existed:
                backup_path = self._backup_name(target)
                shutil.copy2(target, backup_path)
            self._atomic_write(target, content)
            target = self.guard.authorize_existing(str(target), "write").path
            details = {
                "bytes": self._size_utf8(content),
                "overwrote": existed,
                "backup": str(backup_path) if backup_path else None,
            }
            self.audit.write(operation, status="ok", paths=[str(target)], details=details)
            return {"path": str(target), **details}
        except Exception as exc:
            self._audit_error(operation, [path], exc)
            raise

    def edit_text(
        self,
        path: str,
        old_text: str,
        new_text: str,
        *,
        expected_replacements: int = 1,
        backup: bool = True,
    ) -> dict[str, Any]:
        operation = "edit_text"
        try:
            if old_text == "":
                raise ValueError("old_text must not be empty")
            if expected_replacements <= 0:
                raise ValueError("expected_replacements must be positive")
            target = self.guard.authorize_existing(path, "write").path
            if not target.is_file():
                raise IsADirectoryError(str(target))
            if target.stat().st_size > self.settings.limits.max_read_bytes:
                raise ValueError("file exceeds max_read_bytes for text editing")
            try:
                current = target.read_text(encoding="utf-8")
            except UnicodeDecodeError as exc:
                raise ValueError("v0.1 edits UTF-8 text files only") from exc
            count = current.count(old_text)
            if count != expected_replacements:
                raise ValueError(
                    f"replacement precondition failed: expected {expected_replacements}, found {count}"
                )
            updated = current.replace(old_text, new_text, expected_replacements)
            backup_path: Path | None = None
            if backup:
                backup_path = self._backup_name(target)
                shutil.copy2(target, backup_path)
            self._atomic_write(target, updated)
            details = {
                "replacements": expected_replacements,
                "bytes": self._size_utf8(updated),
                "backup": str(backup_path) if backup_path else None,
            }
            self.audit.write(operation, status="ok", paths=[str(target)], details=details)
            return {"path": str(target), **details}
        except Exception as exc:
            self._audit_error(operation, [path], exc)
            raise

    def append_text(self, path: str, content: str) -> dict[str, Any]:
        operation = "append_text"
        try:
            target = self.guard.authorize_existing(path, "write").path
            if not target.is_file():
                raise IsADirectoryError(str(target))
            existing_size = target.stat().st_size
            append_size = self._size_utf8(content)
            if existing_size + append_size > self.settings.limits.max_write_bytes:
                raise ValueError("result would exceed max_write_bytes")
            with target.open("a", encoding="utf-8", newline="") as handle:
                handle.write(content)
                handle.flush()
                os.fsync(handle.fileno())
            self.audit.write(
                operation,
                status="ok",
                paths=[str(target)],
                details={"appended_bytes": append_size},
            )
            return {"path": str(target), "appended_bytes": append_size}
        except Exception as exc:
            self._audit_error(operation, [path], exc)
            raise

    def copy(self, source: str, destination: str, *, overwrite: bool = False) -> dict[str, Any]:
        operation = "copy"
        try:
            src = self.guard.authorize_existing(source, "read").path
            self._assert_no_reparse_tree(src)
            dst = self.guard.authorize_target(destination, "write").path
            if dst.exists():
                if src.is_dir():
                    raise FileExistsError("directory destination must not already exist in v0.1")
                if not overwrite:
                    raise FileExistsError(f"destination exists; set overwrite=true: {dst}")
            self._ensure_parent_for_target(dst, create_parents=False)
            if src.is_dir():
                shutil.copytree(src, dst)
            elif src.is_file():
                shutil.copy2(src, dst)
            else:
                raise ValueError(f"unsupported source type: {src}")
            canonical_dst = self.guard.authorize_existing(str(dst), "write").path
            self.audit.write(operation, status="ok", paths=[str(src), str(canonical_dst)])
            return {"source": str(src), "destination": str(canonical_dst)}
        except Exception as exc:
            self._audit_error(operation, [source, destination], exc)
            raise

    def move(self, source: str, destination: str, *, overwrite: bool = False) -> dict[str, Any]:
        operation = "move"
        try:
            src_read = self.guard.authorize_existing(source, "read").path
            src_delete = self.guard.authorize_existing(str(src_read), "delete").path
            self.guard.assert_not_root(src_delete)
            self._assert_no_reparse_tree(src_delete)
            dst = self.guard.authorize_target(destination, "write").path
            if dst.exists():
                if src_delete.is_dir() or dst.is_dir():
                    raise FileExistsError("directory overwrite is not supported in v0.1")
                if not overwrite:
                    raise FileExistsError(f"destination exists; set overwrite=true: {dst}")
            self._ensure_parent_for_target(dst, create_parents=False)
            if overwrite and dst.exists():
                os.replace(src_delete, dst)
            else:
                shutil.move(str(src_delete), str(dst))
            canonical_dst = self.guard.authorize_existing(str(dst), "write").path
            self.audit.write(operation, status="ok", paths=[str(src_delete), str(canonical_dst)])
            return {"source": str(src_delete), "destination": str(canonical_dst)}
        except Exception as exc:
            self._audit_error(operation, [source, destination], exc)
            raise

    def delete(self, path: str, *, recursive: bool = False) -> dict[str, Any]:
        operation = "delete"
        try:
            target = self.guard.authorize_existing(path, "delete").path
            self.guard.assert_not_root(target)
            if is_reparse_point(target):
                raise AccessDenied(f"deleting reparse points is blocked in v0.1: {target}")
            if target.is_dir():
                if recursive:
                    self._assert_no_reparse_tree(target)
                    shutil.rmtree(target)
                else:
                    target.rmdir()
            elif target.is_file():
                target.unlink()
            else:
                raise ValueError(f"unsupported target type: {target}")
            self.audit.write(
                operation,
                status="ok",
                paths=[str(target)],
                details={"recursive": recursive},
            )
            return {"path": str(target), "deleted": True, "recursive": recursive}
        except Exception as exc:
            self._audit_error(operation, [path], exc)
            raise
