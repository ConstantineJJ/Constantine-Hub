# Constantine Hub — Project Pulse

**Updated:** 2026-09-27  
**Current phase:** PASS B — Blender lifecycle stabilization / Hub core preparation  
**Repository:** `ConstantineJJ/Constantine-Hub`

## Current goal

Build a stable local MCP control plane in this order:

1. Local Files MCP;
2. connect it to ChatGPT through Secure MCP Tunnel;
3. use that local-file access to simplify further Hub development and diagnostics;
4. stabilize/migrate Blender MCP_Con;
5. build Constantine Hub core + GUI;
6. rebuild Godot MCP_R independently from any Godot project;
7. merge control of all bridges under one expandable Hub application.

`my-lab-4-exp` is only a Godot target/laboratory and is not an infrastructure repository. The deleted Stickmans project is not a dependency.

---

## Done — Local Files MCP v0.1 source

Created `adapters/local-files-mcp/` with a Python MCP server based on the current official MCP Python SDK v2 line.

Implemented MCP tools:

- `files_server_info`
- `files_roots`
- `files_exists`
- `files_stat`
- `files_list`
- `files_read_text`
- `files_search_names`
- `files_search_text`
- `files_create_directory`
- `files_write_text`
- `files_edit_text`
- `files_append_text`
- `files_copy`
- `files_move`
- `files_delete`

### Read/search behavior

- absolute paths only;
- explicit allowlisted roots;
- list/tree with bounded entry count;
- UTF-8 text reads with line ranges;
- filename search;
- text search with result and file-size limits;
- recursive traversal does not follow reparse points.

### Write behavior

- create directories;
- create/overwrite UTF-8 files;
- atomic file replacement for write/edit;
- exact edit precondition using expected replacement count;
- optional backup before overwrite/edit;
- append text;
- copy files/directories;
- move/rename;
- explicit delete;
- recursive directory deletion requires `recursive=true`.

### Security / scope

- no arbitrary shell, CMD, PowerShell, or process execution;
- canonical path validation;
- `..` escape rejected;
- existing symlink/junction/reparse targets cannot escape allowed roots;
- copy/move rejects directory trees containing reparse points;
- allowed root itself cannot be deleted or moved destructively;
- per-root permissions: `read`, `write`, `delete`;
- bounded read/write/search sizes;
- mutating operations write JSONL audit records without file contents;
- API keys and machine-specific configuration stay outside Git.

### Runtime/config

Added:

- `config.example.json`;
- `%APPDATA%\ConstantineHub\local-files-mcp.json` as the default local config location;
- `setup.ps1` for creating `.venv`, installing the package and creating the first local config;
- `run_local_files_mcp.cmd` as a stdout-safe stdio launcher;
- dedicated recommended tunnel health port `127.0.0.1:8081` so Blender can keep `8080`.

### Tests / CI

Added Windows unit tests for:

- allowlist authorization;
- outside-root rejection;
- parent (`..`) escape rejection;
- allowed-root destructive-operation guard;
- create/read/edit/append/copy/move/delete round trip;
- filename search;
- text search;
- explicit recursive-delete behavior;
- current MCP Python SDK v2 server import/registration surface.

Added `.github/workflows/local-files-mcp.yml` using `windows-latest`, Python 3.11, compile check and pytest.

**CI PASS:** GitHub Actions run #1 passed compile + unit tests. Run #2 passed after adding the explicit MCP v2 server import test. Run #3 also passed after adding a real stdio MCP handshake / initialize / tool-registration test.

**LIVE QA PASS:** `constantine-files` was initialized against tunnel `tunnel_6ab979e21ccc8191800a285fef498aa0`, Doctor returned `RESULT ok`, the tunnel started on `127.0.0.1:8081`, ChatGPT connected the plugin successfully, and end-to-end MCP calls were executed from ChatGPT. Verified live: server info, roots, create directory, write, read, exact edit with backup, append, copy, move/rename, text search, recursive list, and recursive delete. Test data was removed after QA. `E:\MyCreations` and `F:\My Lab` are currently configured with read/write/delete permissions. PASS A is accepted.

---

## Important architecture decisions

### Constantine Hub and Tools_C stay separate for now

`Constantine-Hub` owns:

- MCP adapters;
- bridge/tunnel runtime;
- process lifecycle;
- health and logs;
- local adapter configuration;
- future unified GUI.

`Tools_C` remains the source for:

- canonical skills;
- contracts;
- workflows;
- QA gates;
- reusable engineering policy.

Do not duplicate ownership between the two repositories.

### Local Files MCP is intentionally not a shell MCP

File access and command execution remain separate capability classes. A future command-execution adapter, if ever added, requires its own explicit design and permissions.

### First bootstrap is manual

The first Local Files MCP connection is intentionally done through PowerShell and `tunnel-client` so the bridge can be proven before Hub automates it. After the Hub core exists, the same profile/process should be managed from the GUI.

### Mandatory Local Files MCP settings UI

When Local Files MCP is integrated into Constantine Hub, it must have a dedicated Settings window for its allowlist. The user must be able to:

- add a folder with a native folder picker;
- remove a configured folder;
- see the canonical resolved path before saving;
- independently toggle `read`, `write`, and `delete` permissions per root;
- refuse duplicate/nested-conflicting entries cleanly;
- refuse invalid/reparse escape roots;
- save to the machine-local Local Files MCP config, never to Git;
- apply changes safely by validating the new config first and then restarting/reloading only the Local Files adapter if required.

The Hub overview should show the number of allowed roots and whether write/delete access is enabled, but detailed path management belongs in the Local Files MCP Settings window.

---

## Next immediate action — PASS B

Use the now-live Local Files MCP to inspect and stabilize `E:\MyCreations\Blender-MCP-Co` before extracting shared Hub runtime code.

Immediate order:

1. fix the Blender MCP_Con tunnel lifecycle bug where a failed/timed-out loopback TCP connect was treated as proof that another tunnel owned the port;
2. keep strict protection against actually occupied/mismatched listeners;
3. add startup verification/retry so `Start All` reports whether the child tunnel became ready instead of only spawning it;
4. preserve app-owned process semantics: Hub/launcher may stop only the process it owns; compatible external tunnels are adopted/read-only and left untouched;
5. record the stabilized behavior as the reference implementation for the future Constantine Hub tunnel/process manager;
6. only then begin extracting shared Hub core/UI code.

---

## PASS A acceptance

**PASS A ACCEPTED — 2026-09-27.**

Observed through the actual ChatGPT connector:

- server/plugin discovery succeeds;
- `files_roots` works;
- list/read/search work;
- create/write/edit work;
- directory creation works;
- copy/move work;
- delete works on test data;
- the adapter runs on health port `8081` without conflicting with Blender on `8080`;
- audit-backed mutation operations are active.

Security boundary tests for outside-root and parent-escape behavior are covered by Local Files MCP unit/CI tests; platform policy may reject deliberately unsafe path probes before they reach the custom MCP, which is an additional outer protection layer rather than a server failure.

---

## After PASS A

1. use Local Files MCP for direct inspection of the local Blender MCP_Con source/runtime;
2. fix its tunnel lifecycle bug and process ownership;
3. extract common lifecycle/tunnel/logging code into Constantine Hub core;
4. make Blender the first full Hub adapter;
5. create Godot MCP_R + project plugin as a new independent adapter;
6. add Godot project-plugin installation/update from Hub;
7. add portable Windows build/release pipeline;
8. hand unusually complex local integration/debug tasks to Codex when useful.

## Known open items

- Local Files MCP currently depends on a manually started `tunnel-client run --profile constantine-files`; Hub must own this lifecycle later.
- Local Files MCP settings are still JSON-only; Hub must provide the dedicated allowed-roots/permissions Settings window described above.
- Blender MCP_Con source has been inspected and its lifecycle bug is localized; source fix/build/runtime regression QA are the current task.
- Hub GUI/core has not started; current Constantine-Hub code is still the first adapter foundation only.
- Godot MCP_R remains to be rebuilt independently from any Godot project.


---

## PASS B work started — Blender MCP_Con stabilization

Local inspection through the live Local Files MCP located the failure in `E:\MyCreations\Blender-MCP-Co\src\BlenderMCPCon`.

### Root cause localized

`TunnelRuntime.ProbeAsync()` treated a timed-out loopback TCP connect as `TunnelState.Occupied`. `StartTunnelAsync()` then interpreted every non-`Free` result as evidence that another tunnel existed and refused to spawn `tunnel-client`, producing the observed `Cannot start another tunnel: Health port 8080 is not responding to a TCP probe.` message even when no tunnel-client process owned the port.

### Local source patch applied

- `TunnelRuntime.cs`: a health listener that does not accept a loopback TCP connection is now treated as `Free`; an actually accepting listener still goes through `/api/status` + `/readyz` identity verification before it can be adopted as `Running`.
- `MainForm.cs`: after spawning app-owned `tunnel-client`, startup now polls readiness for up to 10 seconds and reports `Running`, early child exit, invalid profile, or readiness timeout explicitly.
- Existing ownership rule preserved: only the app-owned child may be stopped; a compatible external tunnel is reused and left untouched by Stop.
- Project version bumped from `0.5.2` to source version `0.5.3`.
- Safety backups were created automatically beside the edited source files.

### QA boundary

The source patch has been reviewed structurally but has **not yet been compiled or cold-start regression-tested on Windows** because Local Files MCP intentionally has no shell/process execution capability. Do not replace the currently working `dist\BlenderMCPCon-v0.5.2\BlenderMCPCon.exe` until a `net10.0-windows` build and runtime test pass.

Required regression sequence for 0.5.3:

1. no tunnel-client running + port 8080 free -> `Start All` must spawn and reach ready;
2. app-owned tunnel already running -> duplicate start suppressed;
3. compatible external `blender-local` tunnel already running -> adopt/reuse, do not spawn a second child;
4. unrelated process/listener on 8080 -> refuse start and do not kill it;
5. child exits before readiness -> report failure rather than showing false Running;
6. Stop Tunnel kills only the app-owned child;
7. Blender bridge and 8/8 skill routing still pass after tunnel recovery.
