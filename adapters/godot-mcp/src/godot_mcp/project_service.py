from __future__ import annotations

from collections import deque
from pathlib import Path
import fnmatch
import os
import shutil
import subprocess
import threading
from typing import Iterable

from .config import GodotSettings

class ProjectService:
    def __init__(self, settings: GodotSettings) -> None:
        self.settings = settings
        self.root = settings.project_root.resolve()
        self._process: subprocess.Popen[str] | None = None
        self._output: deque[tuple[int, str, str]] = deque(maxlen=5000)
        self._seq = 0
        self._lock = threading.Lock()

    def res_to_path(self, res_path: str) -> Path:
        if not res_path.startswith("res://"):
            raise ValueError("Path must begin with res://")
        relative = res_path[6:].replace("/", os.sep)
        candidate = (self.root / relative).resolve()
        try:
            candidate.relative_to(self.root)
        except ValueError as exc:
            raise ValueError("Path escapes the configured Godot project") from exc
        return candidate

    def path_to_res(self, path: Path) -> str:
        return "res://" + path.resolve().relative_to(self.root).as_posix()

    def list_files(self, path: str = "res://", pattern: str | None = None, recursive: bool = True, max_results: int = 500) -> dict:
        base = self.res_to_path(path)
        if not base.exists() or not base.is_dir():
            raise FileNotFoundError(path)
        results: list[dict] = []
        iterator: Iterable[Path] = base.rglob("*") if recursive else base.iterdir()
        for item in iterator:
            if len(results) >= max_results:
                break
            if any(part in {".godot", ".git"} for part in item.relative_to(self.root).parts):
                continue
            if pattern and pattern.lower() not in item.name.lower() and not fnmatch.fnmatch(item.name.lower(), pattern.lower()):
                continue
            results.append({"path": self.path_to_res(item), "type": "directory" if item.is_dir() else "file", "size": None if item.is_dir() else item.stat().st_size})
        return {"path": path, "entries": results, "truncated": len(results) >= max_results}

    def read_text(self, path: str, max_bytes: int = 500_000) -> dict:
        file = self.res_to_path(path)
        if not file.is_file():
            raise FileNotFoundError(path)
        limit = min(max_bytes, self.settings.max_file_bytes)
        size = file.stat().st_size
        if size > limit:
            raise ValueError(f"File is {size} bytes; limit is {limit}")
        raw = file.read_bytes()
        if b"\x00" in raw:
            raise ValueError("Binary file rejected")
        return {"path": path, "content": raw.decode("utf-8"), "size_bytes": size}

    def write_text(self, path: str, content: str, overwrite: bool = False) -> dict:
        file = self.res_to_path(path)
        if file.exists() and not overwrite:
            raise FileExistsError(f"File already exists: {path}")
        encoded = content.encode("utf-8")
        if len(encoded) > self.settings.max_file_bytes:
            raise ValueError("Content exceeds configured file size limit")
        file.parent.mkdir(parents=True, exist_ok=True)
        temp = file.with_suffix(file.suffix + ".constantine.tmp")
        temp.write_bytes(encoded)
        temp.replace(file)
        return {"path": path, "size_bytes": len(encoded), "overwrote": overwrite}

    def _find_godot(self) -> str:
        if self.settings.godot_executable and Path(self.settings.godot_executable).exists():
            return self.settings.godot_executable
        env = os.environ.get("GODOT_EXECUTABLE")
        if env and Path(env).exists():
            return env
        for name in ("godot.exe", "godot", "Godot_v4.7-stable_win64.exe", "Godot_v4.7_win64.exe"):
            found = shutil.which(name)
            if found:
                return found
        candidates = [
            Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "Godot" / "Godot.exe",
            Path(r"C:\Program Files\Godot\Godot.exe"),
            Path(r"C:\Program Files (x86)\Steam\steamapps\common\Godot Engine\godot.windows.editor.x86_64.exe"),
        ]
        for candidate in candidates:
            if candidate.exists():
                return str(candidate)
        raise FileNotFoundError("Godot executable was not found; configure godot_executable")

    def run_project(self, scene_path: str | None = None) -> dict:
        if self._process and self._process.poll() is None:
            return {"started": False, "pid": self._process.pid, "message": "Project is already running"}
        exe = self._find_godot()
        args = [exe, "--path", str(self.root)]
        if scene_path:
            args.append(scene_path)
        self._output.clear()
        self._seq = 0
        self._process = subprocess.Popen(args, cwd=self.root, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace", bufsize=1)
        assert self._process.stdout is not None and self._process.stderr is not None
        threading.Thread(target=self._pump, args=(self._process.stdout, "stdout"), daemon=True).start()
        threading.Thread(target=self._pump, args=(self._process.stderr, "stderr"), daemon=True).start()
        return {"started": True, "pid": self._process.pid, "executable": exe, "scene_path": scene_path}

    def _pump(self, stream, channel: str) -> None:
        for line in iter(stream.readline, ""):
            with self._lock:
                self._seq += 1
                self._output.append((self._seq, channel, line.rstrip("\r\n")))

    def stop_project(self) -> dict:
        if not self._process or self._process.poll() is not None:
            return {"stopped": False, "message": "No managed Godot project process is running"}
        pid = self._process.pid
        self._process.terminate()
        try:
            self._process.wait(timeout=4)
        except subprocess.TimeoutExpired:
            self._process.kill()
        return {"stopped": True, "pid": pid}

    def get_output(self, after: int = 0, limit: int = 300, errors_only: bool = False) -> dict:
        with self._lock:
            rows = [row for row in self._output if row[0] > after]
        if errors_only:
            markers = ("error", "exception", "parse error", "script error", "failed")
            rows = [row for row in rows if row[1] == "stderr" or any(m in row[2].lower() for m in markers)]
        rows = rows[:limit]
        return {"after": after, "items": [{"seq": seq, "stream": stream, "text": text} for seq, stream, text in rows], "next_after": rows[-1][0] if rows else after, "running": bool(self._process and self._process.poll() is None)}

    def git(self, args: list[str], max_chars: int = 120_000) -> dict:
        result = subprocess.run(["git", *args], cwd=self.root, capture_output=True, text=True, encoding="utf-8", errors="replace")
        all_output = result.stdout + result.stderr
        return {"exit_code": result.returncode, "output": all_output[:max_chars], "truncated": len(all_output) > max_chars}

    def git_status(self) -> dict:
        return self.git(["status", "--short", "--branch"])

    def git_diff(self, staged: bool = False, paths: list[str] | None = None, stat: bool = False, max_chars: int = 120_000) -> dict:
        args = ["diff"]
        if staged: args.append("--cached")
        if stat: args.append("--stat")
        if paths:
            safe_paths = []
            for item in paths:
                candidate = (self.root / item).resolve()
                candidate.relative_to(self.root)
                safe_paths.append(str(candidate.relative_to(self.root)))
            args.extend(["--", *safe_paths])
        return self.git(args, max_chars=max_chars)

    def git_log(self, limit: int = 20, path: str | None = None) -> dict:
        args = ["log", f"-{limit}", "--oneline", "--decorate"]
        if path:
            candidate = (self.root / path).resolve()
            candidate.relative_to(self.root)
            args.extend(["--", str(candidate.relative_to(self.root))])
        return self.git(args)

    def git_commit(self, message: str, paths: list[str]) -> dict:
        if not paths:
            raise ValueError("Explicit paths are required")
        safe_paths: list[str] = []
        for item in paths:
            candidate = (self.root / item).resolve()
            candidate.relative_to(self.root)
            safe_paths.append(str(candidate.relative_to(self.root)))
        add = self.git(["add", "--", *safe_paths])
        if add["exit_code"] != 0:
            return {"stage": add, "commit": None}
        return {"stage": add, "commit": self.git(["commit", "-m", message, "--", *safe_paths])}

    def git_push(self, remote: str = "origin", branch: str | None = None, set_upstream: bool = False) -> dict:
        args = ["push"]
        if set_upstream: args.append("--set-upstream")
        args.append(remote)
        if branch: args.append(branch)
        return self.git(args)
