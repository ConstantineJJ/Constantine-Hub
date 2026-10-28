<img src="apps/ConstantineHub/Assets/CH_Ico.png" width="72" alt="Constantine Hub">

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

Helper **0.1.2** is bundled with Hub **0.4.0** and is also available as a standalone
`McpHelper-win-x64.zip` in the same release. It includes its own runtime and works
without Hub or Tools_C. [Standalone usage and API](adapters/mcp-helper/README.md).

Code analysis is read-only and explicitly partial: Helper never executes project
MSBuild/restore/scripts, and does not evaluate all dependencies, conditional project
rules, generated/dynamic code or unsaved editor buffers. Rename is a **preview**;
this release has no apply or shell-execution tool.

## Updates

Hub 0.4.0 adds a Godot project selector on the right. Use **Add project…** to
register an existing `project.godot`, then **Switch** to select it. Hub retains
earlier projects and restarts its own tunnel when needed. An editor serving a
different project is reported explicitly, and commands into that editor are blocked.

The right rail shows each MCP service, its bridge state, and a separate **Stop**
button. Externally managed services retain their ownership protection.

**Files / Файлы** opens the shared inbox at `F:\ChatGPT\Constantine Hub Files`.
Drop files there or use **Добавить…**; Hub copies them without replacing existing
assets, adds the folder to Local Files MCP, and connects its stopped file service.
**Запрос для агента** copies a request containing the selected files and active
project. Paste it into the agent chat to request installation; dropping a file
does not modify the game project automatically.

Closing the window keeps Hub and its services in the tray. Minimize keeps the
normal taskbar window; choose **Exit** in the tray menu to quit. Reopening Hub
restores the existing instance. UI tests and upgrade fixtures do not add tray icons.

Helper 0.1.2 uses a lightweight authenticated health check for idle status;
full MCP verification remains available at Start and Doctor. Routine HTTP logging
is disabled, and Hub bounds its retained log and polls less often in the tray.

### Installing updates

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
