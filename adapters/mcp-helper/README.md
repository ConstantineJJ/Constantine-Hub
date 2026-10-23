# MCP Helper

Independent, read-only C# code navigation and rename preview over MCP.
The executable works without Constantine Hub or Tools_C. Hub optionally manages
the same service package. Version: **0.1.1**.

## Run

Create a local JSON config outside your repository:

```json
{
  "WorkspaceRoot": "C:/Projects/Example",
  "Port": 19273,
  "Token": "replace-with-at-least-32-random-alphanumeric-characters",
  "InstanceId": "standalone"
}
```

`McpHelper.exe stdio --config <path>` starts a client-owned stdio server.
`McpHelper.exe serve --config <path>` starts authenticated Streamable HTTP at
`http://127.0.0.1:19273/mcp`. Pass `Authorization: Bearer <Token>` from your MCP
client. Keep tokens out of command lines and repository files. No tunnel is created.

## Capabilities and limits

Seven tools expose status, API information, outline, symbols, references, impact
and rename preview. Find a symbol, then pass its `symbol_id` and `snapshot_id`
to a reference/impact/preview request (`symbolId` / `expectedSnapshot` arguments).
Rename preview returns proposed edits and exact base-file hashes; it never writes.
A rename that introduces compilation errors returns `E_CONFLICT` without edits;
existing unresolved-dependency diagnostics remain explicit limitations.
There is no apply tool or shell execution.

Roslyn builds an in-memory workspace from UTF-8 `.cs` and `.csproj` files. Project
references are read as XML; MSBuild, restore, generators and analyzers never run.
Results always report **partial** coverage: conditional project rules, external
packages, generated/dynamic code and unsaved editor buffers may be unresolved.
Compilation diagnostics are reported as limitations, not a project build result.

Limits: one configured workspace, 1000 files, 1 MiB/file, 32 MiB input, 30 seconds
per query, 200 results/page and bounded responses. Symlinks, junctions, path escapes,
external project references and XML DTDs are rejected. Positions are zero-based UTF-16.
Use a stable trusted local filesystem; this is not an OS sandbox against another
process racing filesystem changes.

## Build and test

From the Hub repository root, with .NET 10 SDK:

```powershell
dotnet run --project adapters/mcp-helper/tests/McpHelper.Tests -c Release
dotnet build adapters/mcp-helper/src/McpHelper -c Release
python adapters/mcp-helper/tests/protocol_smoke.py adapters/mcp-helper/src/McpHelper/bin/Release/net10.0/McpHelper.dll
```

NuGet dependencies are pinned in project/lock files. The standalone Windows release
includes its own runtime. Source builds require the .NET/ASP.NET Core 10 runtime.
