"""Publish a self-contained standalone Helper package with a verifiable file manifest."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--artifacts", type=Path)
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists():
        raise ValueError("Output must be a new directory")
    output.parent.mkdir(parents=True, exist_ok=True)
    artifacts = (args.artifacts or Path(tempfile.mkdtemp(prefix="mcp-helper-build-"))).resolve()
    module = Path(__file__).resolve().parent
    subprocess.run(["dotnet", "publish", str(module / "src/McpHelper/McpHelper.csproj"), "-c", "Release",
                    "-r", "win-x64", "--self-contained", "true", "-p:RestoreLockedMode=true",
                    "--artifacts-path", str(artifacts), "-o", str(output)], check=True)
    for file in output.glob("*.pdb"):
        file.unlink()
    shutil.copyfile(module / "README.md", output / "README.md")
    assets = json.loads((artifacts / "obj/McpHelper/project.assets.json").read_text(encoding="utf-8-sig"))
    package_folders = [Path(p) for p in assets["packageFolders"]]
    packages = {v["path"] for v in assets["libraries"].values() if v.get("type") == "package"}
    for framework in assets["project"]["frameworks"].values():
        for item in framework.get("downloadDependencies", []):
            bounds = [part.strip() for part in item["version"].strip("[]").split(",")]
            if len(set(bounds)) != 1:
                raise ValueError("Runtime dependency must have an exact version")
            packages.add(item["name"].lower() + "/" + bounds[0])
    licenses = output / "licenses"
    licenses.mkdir()
    for source in (module / "third-party").iterdir():
        shutil.copyfile(source, licenses / source.name)
    notices = []
    for package in sorted(packages):
        folder = next((root / package for root in package_folders if (root / package).is_dir()), None)
        if folder is None:
            raise ValueError("Resolved dependency package not found: " + package)
        notices.append(package)
        dest = licenses / package.replace("/", "-")
        dest.mkdir()
        for source in folder.iterdir():
            if source.is_file() and ("license" in source.name.lower() or "notice" in source.name.lower() or source.suffix == ".nuspec"):
                shutil.copyfile(source, dest / source.name)
    (licenses / "DEPENDENCIES.txt").write_text("Resolved NuGet dependencies. See each package license/nuspec and notices.\n\n" + "\n".join(notices) + "\n", encoding="utf-8")
    files = {p.relative_to(output).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
             for p in sorted(output.rglob("*")) if p.is_file()}
    manifest = {"kind": "mcp-helper-package", "schemaVersion": 1, "version": "0.1.1", "entrypoint": "McpHelper.exe", "files": files}
    (output / "package-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"package": str(output), "version": "0.1.1", "files": len(files)}))


if __name__ == "__main__":
    main()
