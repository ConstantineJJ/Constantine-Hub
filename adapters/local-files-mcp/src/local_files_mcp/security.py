from __future__ import annotations

import os
import stat
from dataclasses import dataclass
from pathlib import Path

from .config import RootRule, Settings


class AccessDenied(PermissionError):
    """Raised when a path or operation falls outside the configured allowlist."""


def is_reparse_point(path: Path) -> bool:
    """Return True for symbolic links and Windows reparse points/junctions."""
    try:
        if path.is_symlink():
            return True
        info = path.lstat()
    except OSError:
        return False

    attributes = getattr(info, "st_file_attributes", 0)
    reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    return bool(attributes & reparse_flag)


def _norm(path: Path) -> str:
    return os.path.normcase(os.path.normpath(str(path)))


def _contains(root: Path, candidate: Path) -> bool:
    try:
        return os.path.commonpath([_norm(root), _norm(candidate)]) == _norm(root)
    except ValueError:
        return False


@dataclass(frozen=True)
class AuthorizedPath:
    path: Path
    rule: RootRule


class AccessGuard:
    def __init__(self, settings: Settings):
        self.settings = settings

    @staticmethod
    def _absolute(raw_path: str) -> Path:
        if not isinstance(raw_path, str) or not raw_path.strip():
            raise AccessDenied("path must be a non-empty string")
        path = Path(os.path.expandvars(raw_path)).expanduser()
        if not path.is_absolute():
            raise AccessDenied(f"relative paths are not allowed: {raw_path}")
        return path

    def _resolve(self, raw_path: str, must_exist: bool) -> Path:
        path = self._absolute(raw_path)
        try:
            return path.resolve(strict=must_exist)
        except FileNotFoundError as exc:
            raise FileNotFoundError(f"path does not exist: {path}") from exc
        except OSError as exc:
            raise AccessDenied(f"cannot canonicalize path {path}: {exc}") from exc

    def _select_rule(self, candidate: Path, permission: str) -> RootRule:
        matching = [
            rule
            for rule in self.settings.allowed_roots
            if _contains(rule.path, candidate) and permission in rule.permissions
        ]
        if not matching:
            raise AccessDenied(
                f"{permission} access denied outside configured roots: {candidate}"
            )
        # The most specific nested root wins when roots overlap.
        return max(matching, key=lambda rule: len(_norm(rule.path)))

    def authorize(
        self, raw_path: str, permission: str, *, must_exist: bool = True
    ) -> AuthorizedPath:
        if permission not in {"read", "write", "delete"}:
            raise ValueError(f"unsupported permission: {permission}")
        candidate = self._resolve(raw_path, must_exist=must_exist)
        rule = self._select_rule(candidate, permission)
        return AuthorizedPath(candidate, rule)

    def authorize_existing(self, raw_path: str, permission: str) -> AuthorizedPath:
        return self.authorize(raw_path, permission, must_exist=True)

    def authorize_target(self, raw_path: str, permission: str) -> AuthorizedPath:
        """Authorize an existing or not-yet-created target path.

        strict=False canonicalization resolves existing symlink/junction components and
        collapses '..'. After parent creation, callers should re-authorize the parent
        strictly before writing to reduce reparse-point race opportunities.
        """
        return self.authorize(raw_path, permission, must_exist=False)

    def ensure_parent(self, target: Path, permission: str = "write") -> AuthorizedPath:
        parent = target.parent.resolve(strict=True)
        rule = self._select_rule(parent, permission)
        return AuthorizedPath(parent, rule)

    def assert_not_root(self, path: Path) -> None:
        normalized = _norm(path)
        for rule in self.settings.allowed_roots:
            if normalized == _norm(rule.path):
                raise AccessDenied(f"refusing destructive operation on allowed root: {path}")
