# Constantine Hub — Project Pulse

**Updated:** 2026-09-27  
**Current phase:** PASS A — Local Files MCP v0.1  
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
- explicit recursive-delete behavior.

Added `.github/workflows/local-files-mcp.yml` using `windows-latest`, Python 3.11, compile check and pytest.

At the time this Pulse entry was written, the first GitHub Actions run had started and was still in progress. Do not call v0.1 runtime-accepted until CI and the real ChatGPT tunnel QA both pass.

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

---

## Next immediate action — user machine

Clone/update `Constantine-Hub`, then from:

```text
adapters\local-files-mcp
```

run `setup.ps1`, inspect `%APPDATA%\ConstantineHub\local-files-mcp.json`, create a new OpenAI tunnel/runtime key, initialize profile `constantine-files` with health port `8081`, run Doctor, start the tunnel, and connect it to ChatGPT.

Use a disposable test root first, for example:

```text
E:\MyCreations\ConstantineHub_MCP_Test
```

Do not begin real working-directory edits until end-to-end create/read/edit/move/delete tests pass through ChatGPT.

---

## Acceptance boundary for PASS A

PASS A is complete only after all of these are observed through the actual ChatGPT connector:

- server/plugin discovery succeeds;
- `files_roots` works;
- list/read/search work;
- create/write/edit work;
- directory creation works;
- copy/move work;
- delete works on test data;
- outside-root access is denied;
- `..` escape is denied;
- audit log contains mutation records;
- tunnel restart does not corrupt the profile;
- no conflict with Blender tunnel on port 8080.

Until then the current status is **SOURCE IMPLEMENTED / LIVE QA PENDING**.

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

- GitHub Actions result still needs to be recorded after completion.
- No live Secure MCP Tunnel exists yet for Local Files MCP.
- No ChatGPT end-to-end file mutation has been executed yet.
- Hub GUI/core has not started; current code is the first adapter foundation only.
