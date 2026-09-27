from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from threading import Lock
from typing import Any


class AuditLogger:
    """Append-only JSONL audit log for mutating filesystem operations."""

    def __init__(self, path: Path):
        self.path = path
        self._lock = Lock()

    def write(
        self,
        operation: str,
        *,
        status: str,
        paths: list[str],
        details: dict[str, Any] | None = None,
    ) -> None:
        record = {
            "time_utc": datetime.now(timezone.utc).isoformat(),
            "operation": operation,
            "status": status,
            "paths": paths,
            "details": details or {},
        }

        self.path.parent.mkdir(parents=True, exist_ok=True)
        line = json.dumps(record, ensure_ascii=False, sort_keys=True)
        with self._lock:
            with self.path.open("a", encoding="utf-8", newline="\n") as handle:
                handle.write(line + "\n")
