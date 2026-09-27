# Constantine Local Files MCP v0.1

Local filesystem MCP server for Constantine Hub.

The server exposes controlled filesystem access to explicitly configured roots. It supports reading, searching, creating, editing, copying, moving, and deleting files/directories, but intentionally does **not** expose arbitrary shell, PowerShell, CMD, or process execution.

## Capabilities

Read:

- list directory / recursive tree;
- stat / exists;
- read UTF-8 text with line ranges;
- search filenames;
- search text in UTF-8 files.

Write:

- create directory;
- create / overwrite UTF-8 file;
- exact text edit with replacement-count precondition;
- append text;
- copy;
- move / rename;
- delete file;
- explicit recursive directory delete.

Safety / scope:

- absolute paths only;
- explicit `allowed_roots`;
- per-root `read`, `write`, `delete` permissions;
- canonical path validation;
- `..` escape blocked;
- existing symlink/junction/reparse targets are canonicalized and cannot escape the allowlist;
- recursive traversal does not follow reparse points;
- destructive operations cannot target an allowed root itself;
- write/read/search size limits;
- append-only JSONL audit log for mutations;
- no secrets stored in the repository.

## 1. Install on Windows

From the cloned `Constantine-Hub` repository:

```powershell
cd .\adapters\local-files-mcp
powershell -ExecutionPolicy Bypass -File .\setup.ps1
```

The setup script creates `.venv`, installs the package and creates the local config if it does not already exist:

```text
%APPDATA%\ConstantineHub\local-files-mcp.json
```

Review that file before first start. The example currently grants full file permissions to:

```text
E:\MyCreations
F:\My Lab
```

Remove or narrow any root you do not want exposed.

## 2. Local config

Example:

```json
{
  "schema_version": 1,
  "allowed_roots": [
    {
      "path": "E:\\MyCreations",
      "permissions": ["read", "write", "delete"]
    }
  ],
  "limits": {
    "max_read_bytes": 2000000,
    "max_write_bytes": 2000000,
    "max_search_file_bytes": 1000000,
    "max_results": 200,
    "max_tree_entries": 2000
  },
  "audit_log": "%APPDATA%\\ConstantineHub\\local-files-audit.jsonl"
}
```

The OpenAI runtime API key is **not** part of this file. It belongs to `tunnel-client` runtime configuration / environment.

## 3. Test the MCP server locally

The stdio launcher is:

```text
adapters\local-files-mcp\run_local_files_mcp.cmd
```

Do not expect normal console output when it is healthy: stdio is the MCP wire.

For SDK-level development you can also use the MCP Inspector from this directory after installing the CLI extra if desired.

## 4. First Secure MCP Tunnel bootstrap

Create a new tunnel in OpenAI Platform and copy its `tunnel_id`.

For the first manual bootstrap, use the existing `tunnel-client.exe`. Example:

```powershell
$tc = "$env:USERPROFILE\chatgpt-blender-mcp\tunnel-client.exe"
$env:CONTROL_PLANE_API_KEY = "YOUR_RUNTIME_KEY"

& $tc init `
  --sample sample_mcp_stdio_local `
  --profile constantine-files `
  --tunnel-id tunnel_YOUR_ID `
  --health-listen-addr 127.0.0.1:8081 `
  --mcp-command "E:/PATH/TO/Constantine-Hub/adapters/local-files-mcp/run_local_files_mcp.cmd"

& $tc doctor --profile constantine-files --explain
& $tc run --profile constantine-files
```

`8081` is reserved for Local Files so the existing Blender tunnel can keep `8080`.

Check in another PowerShell window:

```powershell
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8081/healthz
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8081/readyz
```

Then connect the tunnel as a private/developer MCP plugin in ChatGPT.

## 5. First QA sequence

Use a dedicated test directory inside an allowed root, for example:

```text
E:\MyCreations\ConstantineHub_MCP_Test
```

Required acceptance checks:

1. `files_roots`
2. `files_list`
3. `files_write_text` create
4. `files_read_text`
5. `files_edit_text`
6. `files_search_text`
7. `files_create_directory`
8. `files_copy`
9. `files_move`
10. `files_delete`
11. verify path outside allowlist is rejected
12. verify a `..` escape is rejected
13. inspect `%APPDATA%\ConstantineHub\local-files-audit.jsonl`

Do not enable real working directories until the test folder passes end-to-end through ChatGPT.

## MCP tools

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

## Known v0.1 boundaries

- UTF-8 text first; no binary read/write tool yet.
- No shell/process execution.
- No directory merge/overwrite.
- Copy/move rejects trees containing reparse points.
- No interactive approval layer inside the server yet; scope is controlled by local allowlist permissions.
- File operations are intentionally bounded by configured size/result limits.

The next owner is Constantine Hub itself: once this MCP bridge is proven, Hub will manage its setup, process ownership, tunnel lifecycle, health, logs, and allowed-root configuration through the GUI.
