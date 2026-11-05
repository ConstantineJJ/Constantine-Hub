# Changelog

## 0.5.6-rc.1 — unreleased candidate, 2026-11-05

### Fixed
- Keep confirmed stdio command exits degraded even if the current tunnel still advertises a live wrapper PID and cached green health. A fresh owned process resets the failure; late output from an old process cannot affect its replacement.
- Retain process ownership and abort replacement when Stop fails or exit is not confirmed. Application cleanup still attempts the remaining adapters.
- Limit active Godot tunnel MCP workers to one to prevent confirmed shared-stdio response misrouting and upstream pipe closure. Other adapter launch settings remain unchanged.
- Preserve editor/runtime bridge failure reasons in diagnostics; report connection/status timeouts instead of treating every missing runtime response as idle.
- Describe the actual tunnel/editor recovery checks and runtime availability separately, without claiming an MCP roundtrip.

### Validation
- Controlled process-stop failures, stale-health/terminal-exit fixtures, late process output, ReConnect confirmation/exhaustion/cancellation, bridge timeout diagnostics, concurrent stdio requests and existing application regressions.
- This candidate is not a published stable release. The original production pipe failure and full live Godot endurance remain separate acceptance work.

## 0.5.5 — 2026-11-03

### Added
- Layered, double-buffered Jensen animation in the existing NVIDIA Utility card, driven by the adapter's local MCP and tunnel states.
- Fixed normalized eye anchors: left dollar rotates counterclockwise, right dollar clockwise; independent rotating/pulsing rays, connection bursts and sparkles.
- Smooth connection/loss transitions without restarting unchanged states; suspend frame rendering while hidden, minimized or stably disconnected.
- Build and release verification of all six supplied transparent PNGs, plus a checksum-pinned v0.5.4 upgrade/rollback fixture.

### Changed
- Replace the static legacy Jensen artwork with the six JensenAnimation assets; no legacy or installed-dist resource fallback.
- Keep release asset names and the existing auto-update protocol compatible with v0.5.4.

## 0.5.4 — 2026-11-03

### Added
- NVIDIA App MCP in Utility, with local protocol discovery, diagnostics and ownership-safe tunnel controls.
- Opt-in ReConnect fallback: three cycles of Restart, Restart, Stop/Doctor/Start; cancel, early success and manual rearm after nine failures.
- Stable Windows tray GUID/AppUserModelID and guarded legacy-history preview with explicit exact-entry cleanup.

### Changed
- Product display name is ConHub.; larger logo, two-color title, Home and project chooser in the top bar.
- Keep legacy executable, archive, configuration and profile identifiers for update compatibility.
- Stable releases require an approved manual workflow; upgrade fixtures use the real checksum-pinned v0.3.1 archive.

### Fixed
- Bound Godot traversal/encoding and MCP responses; cancel socket work and reject malformed, oversized or mismatched responses.
- Serialize Godot lifecycle operations and terminate owned Doctor/setup children on cancellation.


## 0.4.4 — 2026-10-31

### Fixed
- Detect an exited MCP stdio child even when tunnel-client keeps `/readyz` and its cached channel probe green.
- Show the unavailable MCP server as degraded and let Start recover its Hub-owned tunnel instead of reporting that it is already ready.
- Preserve external tunnel ownership during recovery and project switching; Doctor uses a separate health port for degraded tunnels too.

## 0.4.3 — 2026-10-30

### Fixed
- Correct log trimming: deleting a protected RichEdit selection was rejected, so the native log kept growing and the failed edit was retried on every new line.
- Render bounded plain-text log snapshots in batches instead of updating and scrolling for every incoming line.
- Keep collecting recent diagnostics in memory while Hub is in the tray or the log panel is collapsed; refresh the view when shown again.
- Disable automatic URL formatting in diagnostic output and apply the same display bound when toggling verbose mode.

## 0.4.2 — 2026-10-29

### Fixed
- Notify when Allowlist folders become unavailable, with actions to remove their entries, open settings, or defer the notification.
- Keep unavailable folders editable; select a new location without changing their read/write/delete permissions.
- Allow removal of every unavailable entry without granting implicit access. Local Files remains stopped until an accessible folder is allowed.
- Recheck folder availability before cleanup, preserve other configuration fields, and keep one previous-config backup.

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
