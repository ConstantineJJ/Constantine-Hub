# Constantine Hub — Project Pulse

Current state as of 2026-10-02 is maintained in [Project-pulse.md](Project-pulse.md).
The entries below are historical snapshots; their pending gates are not the current
Skills / Contracts feature status.

**Updated:** 2026-09-28  
**Current phase:** PASS F — Hub v0.2.1 release / live UI QA pending  
**Repository:** `ConstantineJJ/Constantine-Hub`  
**v0.1.4 release commit:** `b41a70a48e75c7678f58b7931053c4df49b0a5e6`  
**v0.2.1 release commit:** `d380ebed48a4d69b10454fd60ec724a81b603311`

## Mission

Build one expandable Windows control plane for independent MCP adapters. Hub owns local runtime orchestration, process lifecycle, tunnel health, logs, project-plugin installation and machine-local settings. `Tools_C` remains the canonical source of skills, contracts, workflows and QA policy.

Current adapters:

1. Local Files MCP — live;
2. Blender MCP — live;
3. Godot MCP — source + CI pass, live editor/runtime QA next.

`F:\My Lab\my-lab-4-exp` is only a Godot target/laboratory. Infrastructure must never depend on a disposable Godot project.

---

## Accepted architecture invariants

- Each adapter is technically independent even though one Hub controls it.
- A compatible external tunnel may be adopted/read-only; Hub never kills a process it did not start.
- Hub may stop only Hub-owned child processes.
- API keys and machine-specific tunnel/profile state stay outside Git.
- Local Files MCP does not expose arbitrary shell/PowerShell execution.
- Project plugins are thin project-side bridges; servers/tunnels/launchers live in Constantine Hub.
- `Tools_C` is canonical knowledge; deployed skill routers must not become an independent rule source.
- Lower-level structural/CI success never substitutes for engine/runtime/visual QA.

---

## PASS A — Local Files MCP — ACCEPTED

Implemented `adapters/local-files-mcp/` with read/search/create/edit/write/copy/move/delete operations inside explicit allowed roots.

Security boundary:

- canonical absolute paths;
- `..` escape rejection;
- reparse/junction escape protection;
- per-root `read` / `write` / `delete` permissions;
- destructive root guard;
- bounded file/search sizes;
- mutation audit log;
- no arbitrary process execution.

Live ChatGPT QA passed create/read/edit/append/copy/move/search/list/delete round trips.

Current machine roots:

- `E:\MyCreations` — read/write/delete;
- `F:\My Lab` — read/write/delete;
- `D:\Desktop\Constantine Hub docs and backups` — read/write/delete.

Hub now owns the `constantine-files` tunnel successfully. Local Files Settings UI supports native Add Folder, Remove, canonical path display and independent Read/Write/Delete permissions. Saving a Hub-owned config restarts only that adapter; external/adopted tunnels are never killed automatically.

---

## PASS B/C — Blender stabilization + Hub core — ACCEPTED FOR CURRENT USE

Original Blender MCP_Con failure was localized: a failed TCP probe on health port 8080 was incorrectly treated as proof another tunnel occupied the port. Source patch 0.5.3 changed dead/non-accepting listener state to free while preserving mismatch protection.

The standalone launcher is no longer the preferred control path. Constantine Hub now manages Blender MCP directly through shared lifecycle code.

Live state verified 2026-09-28:

- Blender running;
- bridge `127.0.0.1:9876` connected;
- `blender-local` tunnel started by Hub and reached ready;
- direct ChatGPT `get_scene_info` succeeded against the live Blender scene;
- 8/8 Blender skill routers verified.

### Canonical skills/contracts verification

Live `Blender_MCP.read_skill("Blender_Character_Pipeline_Core")` resolved:

- `canonical_root = E:\MyCreations\Tools_C`;
- shared `docs/foundation.md` contract;
- canonical Blender pipeline procedure;
- canonical verification procedure.

Important distinction: Hub's visible `8/8 verified` status validates deployed/source router parity. The live MCP skill-read path is what proves canonical Tools_C contract/context loading. Future UI polish may expose a separate `Contracts: canonical Tools_C OK` status.

---

## Constantine Hub v0.1.2 state

Implemented .NET 10 WinForms Hub core:

- adapter contract/state model;
- tunnel-client discovery and profile loading;
- health/identity/readiness verification;
- startup wait + child-exit detection;
- Hub-owned process tracking;
- external/adopted process protection;
- Doctor output;
- shared log + Save Log;
- Start All / Stop Hub-Owned;
- system tray minimize/restore and tray actions;
- supplied Constantine Hub icon.

Readability pass v0.1.2:

- main non-log UI font increased ~50%;
- status rows enlarged with vertical breathing room;
- buttons/cards enlarged;
- header enlarged;
- log panel intentionally stays `Cascadia Mono 9.5 pt` for dense diagnostics.

Windows CI builds/publishes portable `win-x64` successfully.

---

## PASS D — Godot MCP rebuild — SOURCE + CI PASS

Godot was rebuilt from scratch as an independent adapter under `adapters/godot-mcp/`.

### MCP server

Python stdio MCP server now preserves the previous Godot_MCP_R public capabilities:

- status;
- editor/runtime scene tree;
- node inspect/create/set/delete;
- attach script;
- save/open scene;
- project file list/read/write;
- managed project run/stop/output/errors;
- runtime screenshot capture;
- Input Map action injection;
- bounded public node method calls;
- Git status/diff/log/explicit-path commit/push;
- safe extended editor catalogue/call.

Added canonical workflow tools:

- `godot_list_skills`;
- `godot_read_skill`;
- `godot_get_skill_context`.

Canonical Godot context requires:

- `Tools_C/docs/foundation.md`;
- `Tools_C/skills/godot-project/SKILL.md`;
- `Tools_C/skills/godot-asset-integration/SKILL.md`;
- `Tools_C/skills/verification/SKILL.md`.

### Godot project plugin

Created `project-addon/addons/constantine_mcp/`:

- `plugin.cfg`;
- `constantine_mcp_plugin.gd`;
- editor TCP JSON-line bridge on `127.0.0.1:6262`;
- runtime/autoload bridge on `127.0.0.1:6263`;
- JSON-safe Godot Variant codec.

The editor bridge supports scene/node editing and bounded editor operations. The runtime bridge supports runtime inspection, screenshot capture, input actions and bounded node calls.

Normal editor/plugin teardown deliberately does **not** remove the runtime autoload; installation/removal ownership belongs to Hub.

### Hub Godot adapter

Created `apps/ConstantineHub/Adapters/Godot/` with:

- machine-local `%APPDATA%\ConstantineHub\godot-mcp.json`;
- target project validation;
- Godot executable discovery;
- addon install/update into the selected project;
- preservation of existing `[editor_plugins]` entries;
- canonical Tools_C skills/contracts verification before start;
- first-run Python venv provisioning;
- optional Godot editor launch;
- editor/runtime bridge diagnostics;
- tunnel lifecycle through shared Hub core.

Current first-pass target defaults:

- project: `F:\My Lab\my-lab-4-exp`;
- tunnel profile: `godot-local`;
- reused existing ChatGPT Godot tunnel ID: `tunnel_6aa32f76e3f881919f5757c792036950`;
- health port: `8082`;
- editor bridge: `6262`;
- runtime bridge: `6263`.

Hub Godot card now shows Project / Plugin / Editor bridge / Runtime bridge / Tunnel / Skills-contracts and exposes Start / Stop / Restart / Install Plugin / Doctor.

### Godot CI

PR #2 first smoke found only a test API-name mismatch (`CallToolResult.is_error`, not `isError`). Runtime code did not require a fix.

Corrected run PASS:

- package install;
- Python compileall;
- real stdio MCP initialize;
- list_tools;
- real tool calls;
- all 29 expected Godot tools registered;
- `res://../` traversal regression blocked;
- project-addon payload present;
- Hub .NET restore/build/publish;
- published Hub artifact contains Godot launcher + addon.

PR #2 was squash-merged to `main` as `354caf4c2a348cee6868e98b6c84bd6f0b88d687`.

Main post-merge workflows PASS:

- Godot MCP run `36358032931`;
- Constantine Hub run `36358032965`.

Artifact `ConstantineHub-win-x64` from main includes the Godot adapter.

---

## Current open gate — LIVE GODOT QA

Do not mark Godot accepted until actual Godot 4.7 evidence passes.

Next sequence:

1. run the new main artifact;
2. Hub installs/enables `Constantine MCP` in `F:\My Lab\my-lab-4-exp`;
3. reload/restart the Godot project once if needed so the editor plugin activates;
4. confirm editor bridge `127.0.0.1:6262`;
5. Hub creates/reuses `godot-local` with health port 8082 and the existing Godot tunnel ID;
6. existing ChatGPT `Godot_MCP_R` connector should become live again;
7. call `godot_status`;
8. verify `godot_list_skills` / `godot_get_skill_context` resolves canonical Tools_C;
9. scene-tree read;
10. create a temporary node, inspect/set it, then delete it;
11. save/open scene test only on disposable QA scene;
12. run project, confirm runtime bridge 6263;
13. capture game screenshot and test a safe Input Map action if one exists;
14. review Godot Output/errors and close regressions;
15. only then accept Godot PASS D.

## Known follow-ups after Godot live PASS

- add dedicated Godot Settings UI with project picker, tunnel/profile fields and auto-launch toggle instead of relying on the first-pass default config;
- expose a separate canonical-contract status for Blender in Hub;
- retire standalone Blender MCP_Con after sufficient Hub cold-start/restart QA;
- add release packaging/patch/update ergonomics after adapter behavior stabilizes.

---

## PASS E — Constantine Hub v0.1.4

Requested after live v0.1.3 QA.

Implemented locally for the v0.1.4 branch:

- synchronized project version to `0.1.4`;
- compact UI pass: main UI font reduced from 15 pt to 13.5 pt, card/status/button spacing tightened so the complete Godot card fits without vertical scrolling at the normal window size; log font remains `Cascadia Mono 9.5 pt`;
- Godot runtime bridge idle state changed from red failure semantics to amber `Idle / runtime not running` when no runtime session is active;
- Normal/Verbose log mode added: Normal hides tunnel-client FX/JSON noise, Verbose reveals it; Save Log always writes the complete diagnostic history;
- stable update check against GitHub Releases added on startup and through `Check Updates`;
- update package requires both `ConstantineHub-win-x64.zip` and `ConstantineHub-win-x64.sha256`; SHA-256 is verified before installation;
- added separate `ConstantineHub.Updater.exe` bootstrapper so the running Hub never tries to replace itself;
- updater waits for the old Hub to exit, backs up replaced files, copies the staged payload atomically per file, restarts the new Hub and waits for an explicit startup marker;
- if the new build fails to report startup within the timeout, updater restores the previous files and restarts the rolled-back Hub;
- updater touches installation payload only; user configuration remains under `%APPDATA%` / `%LOCALAPPDATA%` outside the release payload;
- CI release pipeline now publishes a zipped portable package plus SHA-256. On a new semantic version in `main`, it creates the stable `vX.Y.Z` GitHub Release; each main build also creates a prerelease `vX.Y.Z-dev.<run_number>` as a future Dev-channel foundation.

Review note: the repository had drifted behind the live binary (`main` still declared v0.1.2 while the tested application identified as v0.1.3). v0.1.4 is the reconciliation point: source version, CI artifact and release tag must match before the updater is accepted.

Acceptance gate for v0.1.4:

1. PR CI must build both Hub and Updater;
2. published package must contain `ConstantineHub.Updater.exe` and the Godot adapter payload;
3. main CI must create `v0.1.4` release assets with matching SHA-256;
4. current v0.1.3 installation must detect `v0.1.4`;
5. in-app update must restart into v0.1.4 and preserve Local Files/Blender/Godot machine settings;
6. one forced bad-start test should confirm rollback before updater PASS is final.

### v0.1.4 repository/release status

- PR #4 squash-merged to `main` as `b41a70a48e75c7678f58b7931053c4df49b0a5e6`.
- PR CI initially caught `CS0201` in `UpdateService`; fixed before merge. Final PR CI PASS.
- Main workflow run `36362991496` PASS: restore, Hub + Updater build, portable publish, bundled Godot/updater verification, release packaging, artifact upload, stable/dev release publishing.
- Stable GitHub Release `v0.1.4` published with `ConstantineHub-win-x64.zip` and `ConstantineHub-win-x64.sha256`.
- Dev prerelease foundation also published as `v0.1.4-dev.20`.
- First migration from the currently installed v0.1.3 to v0.1.4 is manual because v0.1.3 predates the updater. From v0.1.4 onward the Stable updater path can be tested in-app.
- Acceptance gates 1–3 are PASS. Gates 4–6 remain live-machine QA: updater discovery on a later release, settings preservation/restart, and forced rollback test.

---

## Patch v0.1.5 — DPI-safe status layout + updater test

Live v0.1.4 QA on the user's Windows high-DPI desktop exposed a second layout regression: status text inside all three MCP cards was clipped vertically, and longer left-column labels such as `Allowed roots`, `Editor bridge`, `Runtime bridge` and `Skills/contracts` could be clipped horizontally.

Root cause: v0.1.4 compacted status geometry with hard-coded `30 px` row heights and a fixed `180 px` label column while keeping a 13.5 pt form font under `AutoScaleMode.Dpi`. At the user's DPI/font metrics, the rendered text exceeded those fixed cells.

v0.1.5 fix:

- replace the fixed status-row height with a DPI-aware height derived from `TextRenderer.MeasureText(..., Font)` plus safety padding;
- size each card row from the measured status-row height so the Local Files / Blender / Godot cards remain internally consistent;
- change the left status-name column from fixed `180 px` to `AutoSize` and make name labels participate in preferred-size calculation;
- keep the value column percentage-based so long status values still get the remaining width;
- increase the normal/minimum window height modestly so all three cards, including the six-row Godot card, remain fully visible while preserving a useful log pane;
- bump both Hub and Updater to `0.1.5`.

Release goal: merge only after Windows CI passes Hub + Updater build/publish/package checks. Once stable `v0.1.5` exists, use the already-installed v0.1.4 `Check Updates` path to perform the first real in-app Stable auto-update test, including SHA-256 verification, restart, settings preservation and updater startup handshake.

---

## PASS F — Constantine Hub v0.2.0 UI shell pass

Branch: `ui/v0.2.0-shell-pass`  
PR: `#6`  
Initial UI commit: `8a5ef4b2d607c5a94ddcfa55f35f1d0dabd19c88`

Implemented as a presentation-layer shell over the existing `MainForm` controls rather than a rewrite of adapter/runtime services:

- `UI_Small`-inspired dark engineering-tool shell with left navigation, top status bar, center dashboard and right status rail;
- compact/expanded Local Files / Blender / Godot cards; expanded cards reuse the existing status controls and action handlers;
- compact Start actions forward to the existing per-adapter Start controls;
- diagnostics log is collapsible; existing Normal/Verbose and Save Log controls are retained in the quick-actions rail;
- top-level version, update state, project/profile and Settings access added;
- regular watermelon cat added as runtime window/taskbar/tray icon source and dashboard brand asset;
- waving watermelon cat added to the dashboard welcome area; the committed dashboard copy is an optimized 64×64 derivative of the supplied 128×128 source;
- Hub and Updater version synchronized to `0.2.0`;
- existing `CH_Icon.ico` remains the compile-time executable icon fallback; runtime window/taskbar/tray identity now prefers the new regular-cat PNG.

Preservation boundary:

- no adapter implementation or tunnel ownership logic was changed;
- Local Files allowlist permissions remain in the existing Settings form;
- Start / Stop / Restart / Doctor handlers are reused, not reimplemented;
- Start All / Stop Managed semantics are reused;
- external/adopted process protection remains in the existing core;
- updater service/protocol, release asset names, SHA-256 verification, rollback/startup handshake and AppData storage locations were not changed.

Verification performed:

- PR diff reviewed: seven implementation files in the initial commit, with UI work isolated to the new shell plus icon/version integration;
- GitHub Actions run `36369191688` PASS on `windows-latest`;
- .NET 10 Restore PASS;
- Hub/Updater Build PASS;
- portable `win-x64` Publish PASS;
- bundled Godot adapter + updater verification PASS;
- release packaging and CI artifact upload PASS;
- stable/dev publishing correctly SKIP on the PR branch.

Open gates / risks — do not mark these PASS without live evidence:

- live Windows visual QA of the new shell has not yet run;
- high-DPI clipping/scroll behavior needs inspection on the user's actual display scaling;
- compact/expanded card behavior and all reused buttons need live click-through QA;
- Local Files / Blender / Godot runtime statuses need live confirmation in the new shell;
- the Stable `v0.2.0` self-update, startup handshake and AppData preservation can only be accepted after `main` publishes the release and the installed Hub performs the update;
- the runtime PNG-derived icon path is compiled and packaged by CI, but taskbar/tray appearance still needs live Windows inspection;
- top Settings is a convenience menu over existing actions; navigation remains intentionally shallow in this pass rather than introducing placeholder-only pages.

Recommended next step:

1. merge PR #6 only while its CI remains green;
2. let the `main` workflow publish stable `v0.2.0`;
3. update the installed Hub through its own Stable updater;
4. perform live DPI/visual/action/status QA and record screenshots/results;
5. fix only reproducible UI regressions found in that pass; do not mix them with adapter lifecycle redesign.

### v0.2.0 main release + v0.2.1 identity follow-up

Repository/release verification completed after the initial PASS F handoff:

- PR #6 was squash-merged to `main` as `1a12fb977759945dbdf0d5cf84e683dea4777227`;
- main workflow run `36369483079` PASS, including Restore, Hub + Updater Build, portable `win-x64` Publish, bundled Godot/updater verification, release packaging and stable/dev publishing;
- stable `v0.2.0` and dev `v0.2.0-dev.25` were published with ZIP + SHA-256 assets;
- inspection of the packaged v0.2.0 payload confirmed the regular/waving PNG assets and exposed one identity mismatch: runtime window/taskbar/tray used the new regular cat, while the compile-time executable `CH_Icon.ico` was still the earlier icon;
- follow-up PR #7 replaced the compile-time ICO with a 32×32 ICO generated from the same supplied regular-cat PNG and bumped Hub + Updater together to `0.2.1`;
- PR #7 workflow run `36370279965` PASS; its packaged `CH_Icon.ico` was decoded and compared against the regular PNG downsampled to 32×32 with zero pixel difference;
- PR #7 was squash-merged to `main` as `d380ebed48a4d69b10454fd60ec724a81b603311`;
- main workflow run `36370408501` PASS with every build/package/release step green;
- stable `v0.2.1` and dev `v0.2.1-dev.27` were published. Stable assets are `ConstantineHub-win-x64.zip` and `ConstantineHub-win-x64.sha256`; the release ZIP digest reported by GitHub is `sha256:9ca895d98d5447d4a0b98fc9a2ad5ab18843ebc404287c6dddbb70b5c026d61e`;
- the final main CI artifact was unpacked and inspected: Hub + Updater executables, both watermelon-cat assets and the bundled Godot adapter payload are present; `CH_Icon.ico` is the regular cat and matches the packaged `WatermelonCat.png` at 32×32 exactly.

Still open — live-machine evidence only:

- install/update the currently running Hub to stable `v0.2.1` through its own updater;
- confirm updater startup handshake and preservation of Local Files / Blender / Godot machine settings;
- inspect the new shell on the user's actual Windows DPI/scaling for clipping, unwanted scrollbars and card sizing;
- click through compact/expanded cards plus Start / Stop / Restart / Doctor / Settings / Verbose / Save Log;
- confirm live Local Files, Blender and Godot status rendering;
- confirm regular-cat appearance in Explorer/window/taskbar/tray and waving-cat appearance on Home.

Do not mark PASS F live-accepted until those checks are observed.
