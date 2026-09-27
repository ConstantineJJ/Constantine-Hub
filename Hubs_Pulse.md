# Constantine Hub — Project Pulse

**Updated:** 2026-09-28  
**Current phase:** v0.1.3 layout correction; PASS D live runtime gate remains open
**Repository:** `ConstantineJJ/Constantine-Hub`  
**Layout fix base:** main `ef42e67eb05778f1a82d628a7c3611816ed90ee9`

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

## Constantine Hub v0.1.3 — font/DPI layout correction

Evidence: supplied `Constantine-Hub-log-2026-09-28_023019.md` and screenshots
`022718`, `022842`, `023031`. Local v0.1.2 MainForm matches main's layout (only
an unrelated tray catch comment differs). The existing local checkout is dirty
and hosts deployed files; work was isolated in a fresh main clone.

Root cause: fixed 36-pixel rows minus 8 pixels of vertical margins leave only
28 pixels for 15 pt text, which grows beyond that at higher DPI/text scaling.
Fixed card heights also constrain a percentage-sized content region: Godot's
six 36-pixel rows need 216 pixels, but its 320-pixel card leaves only 182 after
outer spacing, padding, title and actions. The final Skills/contracts row was
hidden. Fixed button sizes also clipped captions. Dpi mode alone cannot make
these mutually inconsistent constraints fit; the form lacked an explicit
96-DPI design baseline. Construction now suspends layout until the full control
tree exists, preventing initial DPI scaling from being consumed before cards
and the log row have been added.

Changes:

- AutoSize rows, content, titles and cards; sufficient margins/padding.
- Label column measures its text; button sizes measure their captions and padding.
- Wrapped actions measure at the allocated width, avoiding spurious empty rows.
- Explicit 96-DPI baseline and PerMonitorV2 application mode.
- Card viewport scrolls when the window cannot fit all content.
- Relayout preserves user scrolling instead of snapping back to the already
  focused action; changing keyboard focus still reveals the new action.
- Log keeps Cascadia Mono 9.5 pt, colors and no-wrap behavior; a separate
  280-logical-pixel row preserves approximately the supplied 420-pixel viewport
  at 150% and is unaffected by growing cards or UI fonts.
- Godot Pass 1, adapters, profile handling and process ownership code unchanged.

Validation:

- PASS: Release build/publish; Godot launcher and addon bundled.
- PASS: 24 native WinForms geometry/render cases (simulated DPI 100/125/150/200%,
  15/18/22.5 pt, narrow/wide windows), including long status refreshes and scrolling.
- PASS: current-monitor PerMonitorV2 layout at actual 144 DPI (150%), including
  a live font/width change; native rendered PNGs inspected.
- PASS: Godot stdio initialize/list/call, all 29 tools; Local Files 9 tests.
- PASS: GitHub CI for `f262734480a8d7889758cfb25662a759ae50ec20` in
  [PR #3](https://github.com/ConstantineJJ/Constantine-Hub/pull/3):
  [Hub build/layout/publish](https://github.com/ConstantineJJ/Constantine-Hub/actions/runs/36360182965),
  [Godot MCP](https://github.com/ConstantineJJ/Constantine-Hub/actions/runs/36360182883),
  [Local Files MCP](https://github.com/ConstantineJJ/Constantine-Hub/actions/runs/36360182871).
  Hub run publishes `ConstantineHub-win-x64` and `ConstantineHub-layout-evidence`.
  Portable remains framework-dependent and requires .NET 10 Desktop Runtime x64.
  PR is prepared; merge/deployment is not part of this layout pass.
- SKIP: physical multi-monitor DPI transitions and Windows accessibility setting
  changes; fresh engine/runtime/input acceptance is not claimed by layout tests.

Supplied runtime evidence confirms all three tunnels reached ready under Hub
ownership, Godot plugin enabled, and Editor Bridge connected on 6262. Runtime
Bridge was stopped/not connected in the screenshot. This advances the editor
startup evidence but does not close the live Godot runtime gate below.

## Constantine Hub v0.1.2 baseline

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
