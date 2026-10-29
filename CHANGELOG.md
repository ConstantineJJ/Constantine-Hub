# Changelog

## 0.4.1 — 2026-10-29

### Fixed
- Hub opens when the selected Godot project has moved, is unavailable, or has not been selected yet. Add or switch projects from the sidebar.
- Check the project before starting Godot MCP or installing its addon, without recreating missing project folders or silently selecting another project.
- An unavailable Local Files folder no longer makes a successful Godot project switch appear to fail; its access issue is logged separately.

## 0.4.0 — 2026-10-28

### Added
- Register and switch Godot project folders from the right sidebar; previous projects remain available.
- Show each MCP service and its bridge state with an independent Stop button.
- Drag-and-drop file inbox at `F:\ChatGPT\Constantine Hub Files`, with non-overwriting copies and agent access through Local Files MCP.
- Copy an asset-installation request with the selected file and active project.

### Changed
- Use the supplied CH_Ico artwork for the executable, window, tray and navigation icon.
- Close the window to the tray; Minimize uses the ordinary taskbar.
- Reopening Hub restores its existing instance. Tests do not register tray icons.

### Fixed
- MCP Helper 0.1.2 uses authenticated idle health checks instead of repeated MCP initialization, and avoids routine HTTP log flooding.
- Bound Hub log retention and reduce background polling while hidden.
- Reject Godot bridge commands when the editor belongs to another project.
- Retire obsolete Helper runtime files during updates, with backups for rollback.
