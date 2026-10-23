<img src="apps/ConstantineHub/Assets/WatermelonCat.png" width="72" alt="Constantine Hub">

# Constantine Hub

A Windows control panel for **Local Files MCP, Blender MCP, Godot MCP and MCP Helper**.
Manage local connections, inspect their health and keep Hub updated from GitHub Releases.

[Download the latest stable release](https://github.com/ConstantineJJ/Constantine-Hub/releases/latest)

## Install

1. Install the .NET 10 Desktop Runtime for Windows x64.
2. Download `ConstantineHub-win-x64.zip` and its `.sha256` from Releases.
3. Verify the checksum, extract into a writable folder and run `ConstantineHub.exe`.
4. Open Settings and configure the adapters you use. Blender/Godot and their bridge
   add-ons remain separate installations.

## What Hub manages

| Module | Purpose |
|---|---|
| Local Files MCP | Scoped local filesystem access and connection settings |
| Blender MCP | Blender bridge/tunnel status, lifecycle and canonical Skills checks |
| Godot MCP | Project/plugin setup, editor/runtime bridge status and connection lifecycle |
| MCP Helper | Independent C# navigation, semantic references, impact and rename preview |

Connections started outside Hub remain **externally managed**. Hub does not take
ownership of them or stop their processes. Start/Stop/Restart operate on owned
instances; Doctor reports configuration and connection health.

## MCP Helper

Expand Blender details to find Helper on the right. Select **Configure**, choose
the C# workspace, save and select **Start Helper**. **Connection** copies the endpoint
and authentication for your MCP client. Keep that connection data private.
Global Start/Stop participation is opt-in in Helper configuration.

Helper **0.1.0** is bundled with Hub **0.3.0** and is also available as a standalone
`McpHelper-win-x64.zip` in the same release. It includes its own runtime and works
without Hub or Tools_C. [Standalone usage and API](adapters/mcp-helper/README.md).

Code analysis is read-only and explicitly partial: Helper never executes project
MSBuild/restore/scripts, and does not evaluate all dependencies, conditional project
rules, generated/dynamic code or unsaved editor buffers. Rename is a **preview**;
this release has no apply or shell-execution tool.

## Updates

Use **Check Updates** to find a newer stable release. After confirmation Hub
downloads the release ZIP/checksum and runs its Updater to replace and restart the
application. Prereleases are ignored by the stable updater.

## Build and test

Windows, .NET 10 SDK and Python 3.12+ are required. From a trusted checkout:

```powershell
dotnet build apps/ConstantineHub -c Release
dotnet run --project apps/ConstantineHub.SmokeTests -c Release
dotnet run --project adapters/mcp-helper/tests/McpHelper.Tests -c Release
python adapters/mcp-helper/package.py --output artifacts/McpHelper-win-x64
python adapters/mcp-helper/tests/protocol_smoke.py artifacts/McpHelper-win-x64/McpHelper.exe
dotnet run --project apps/ConstantineHub.SmokeTests -c Release -- --helper-package artifacts/McpHelper-win-x64
```

The existing GitHub Actions workflow builds the Hub/Updater, verifies the independent
Helper, exercises upgrade/recovery and packages the release. Source tests and CI
remain in the repository; development reports and task logs are kept externally.
