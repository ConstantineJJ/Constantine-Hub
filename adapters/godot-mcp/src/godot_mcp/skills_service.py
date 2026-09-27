from __future__ import annotations

from pathlib import Path

from .config import GodotSettings

class GodotSkillsService:
    SKILLS = ("godot-project", "godot-asset-integration", "verification")

    def __init__(self, settings: GodotSettings) -> None:
        self.root = settings.tools_c_root

    def _require_root(self) -> Path:
        if self.root is None or not self.root.exists():
            raise RuntimeError("Tools_C canonical root was not found; configure tools_c_root")
        return self.root

    def list_skills(self) -> dict:
        root = self._require_root()
        items = []
        for name in self.SKILLS:
            file = root / "skills" / name / "SKILL.md"
            items.append({"name": name, "file": str(file), "exists": file.exists(), "size_bytes": file.stat().st_size if file.exists() else 0})
        foundation = root / "docs" / "foundation.md"
        return {
            "canonical_root": str(root),
            "foundation_contract": str(foundation),
            "foundation_exists": foundation.exists(),
            "skills": items,
            "verified": foundation.exists() and all(item["exists"] for item in items),
        }

    def read_skill(self, name: str) -> dict:
        if name not in self.SKILLS:
            raise ValueError(f"Unknown Godot skill: {name}")
        root = self._require_root()
        foundation = root / "docs" / "foundation.md"
        skill = root / "skills" / name / "SKILL.md"
        if not foundation.exists() or not skill.exists():
            raise FileNotFoundError("Canonical Tools_C skill or foundation contract is missing")
        blocks = [
            f"SOURCE: docs/foundation.md\n\n{foundation.read_text(encoding='utf-8')}",
            f"SOURCE: skills/{name}/SKILL.md\n\n{skill.read_text(encoding='utf-8')}",
        ]
        if name in {"godot-project", "godot-asset-integration"}:
            verification = root / "skills" / "verification" / "SKILL.md"
            if verification.exists():
                blocks.append("SOURCE: skills/verification/SKILL.md\n\n" + verification.read_text(encoding="utf-8"))
        return {"name": name, "canonical_root": str(root), "content": "\n\n\n".join(blocks)}

    def context_for_task(self, task: str) -> dict:
        text = task.lower()
        selected = ["godot-project"]
        if any(token in text for token in ("glb", "asset", "import", "skeleton", "animation", "material", "mesh")):
            selected.insert(0, "godot-asset-integration")
        selected.append("verification")
        unique = []
        for name in selected:
            if name not in unique:
                unique.append(name)
        chunks = [self.read_skill(name)["content"] for name in unique]
        return {"task": task, "skills": unique, "canonical_root": str(self._require_root()), "content": "\n\n===== NEXT GODOT CONTEXT =====\n\n".join(chunks)}
