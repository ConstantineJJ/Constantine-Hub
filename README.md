<img src="apps/ConstantineHub/Assets/CH_Ico.png" width="72" alt="ConHub.">

# ConHub.

A Windows control panel for **Local Files MCP, Blender MCP, Godot MCP, NVIDIA App MCP and MCP Helper**.
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
| NVIDIA App MCP | Loopback MCP discovery, owned tunnel lifecycle and diagnostics in Utility |
| MCP Helper | Independent C# navigation, semantic references, impact and rename preview |

Connections started outside Hub remain **externally managed**. Hub does not take
ownership of them or stop their processes. Start/Stop/Restart operate on owned
instances; Doctor reports configuration and connection health.

## MCP Helper

Expand Blender details to find Helper on the right. Select **Configure**, choose
the C# workspace, save and select **Start Helper**. **Connection** copies the endpoint
and authentication for your MCP client. Keep that connection data private.
Global Start/Stop participation is opt-in in Helper configuration.

Helper **0.1.2** is bundled with ConHub. **0.5.5** and is also available as a standalone
`McpHelper-win-x64.zip` in the same release. It includes its own runtime and works
without Hub or Tools_C. [Standalone usage and API](adapters/mcp-helper/README.md).

Code analysis is read-only and explicitly partial: Helper never executes project
MSBuild/restore/scripts, and does not evaluate all dependencies, conditional project
rules, generated/dynamic code or unsaved editor buffers. Rename is a **preview**;
this release has no apply or shell-execution tool.

## Updates

ConHub. 0.5.4 places Home and the Godot project selector in the top bar. Use **Add project…** to
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

### ReConnect fallback

ReConnect is off until clicked. Its header button stays visible when the Godot
card is collapsed. Green means monitoring, red means confirmed connection failure
and recovery, yellow means nine attempts exhausted. Each of three cycles runs
Restart, Restart, then Stop → Doctor → Start using existing ConHub. operations.
Every operation completes before its status check; success clears the counter.
Failures are confirmed by another probe; retries wait 10 seconds (confirmation 5
seconds). Idle time and individual MCP tool errors do not trigger recovery. New
critical Godot log messages only prompt a status check. Old log files are never
replayed. Another click disables and cancels automatic work; clicking yellow rearms
one new series. Manual Godot operations and project switches share the same lock.

### Windows notification area

ConHub. retains `ConstantineHub.exe`, its installation folder, updater asset names,
settings keys and tunnel profiles for compatibility. Extract updates into the same
folder. The notification GUID and AppUserModelID are version independent; smoke
and isolated upgrade runs never register an icon. Unsigned Windows GUID icons are
bound to the executable path, so moving the application can prevent registration.
ConHub. reports this rather than creating a replacement identity.

**Tray history…** in the tray menu previews related `NotifyIconSettings` entries.
Only verified inactive legacy entries from the exact installation path are eligible
for explicit, individually confirmed removal, with a typed-value backup and a fresh
ownership check. Unknown historical paths stay preview-only. Other applications,
current identity, promotion preferences and whole system caches are never reset.

### NVIDIA App MCP

The Utility card uses the six transparent PNG layers in `Assets/JensenAnimation`.
Its happy face and effects require both the local MCP handshake and a ready tunnel;
a starting tunnel shows a sad face with a gentle pulse, and a stopped or failed
connection fades back to the sad face. Dollars turn around fixed normalized eye
anchors (left counterclockwise, right clockwise), with independent rays, bursts
and sparkles. The 30 FPS frame timer pauses when hidden, minimized or disconnected;
it performs no connection polling. It reports the actual local
`nvidia-app` MCP handshake and tools/list separately from tunnel readiness. Local
HTTP stays on `127.0.0.1`; ConHub. does not launch or stop NVIDIA's external service.
Configure the existing dedicated `constantine-nvidia` tunnel profile to enable
agent access. [Setup and verification](adapters/nvidia-app/README.md).

### Installing updates

**0.5.6** fixes confirmed Godot stdio connection failures. Its investigation and
acceptance limits are recorded in [the review report](docs/reviews/godot-mcp-2026-11-05-final.md).
ReConnect remains opt-in: two failed connection checks precede the existing
Restart, Restart, Stop/Doctor/Start sequence, repeated at most three times.
While ReConnect is enabled it also restores a manually stopped Godot MCP.
Turn ReConnect off first when you intend to keep MCP stopped. Turning it off
cancels active recovery and scheduled retries; it never enables itself.
Runtime Bridge availability is independent
of tunnel/editor readiness; a stopped game does not require restarting the tunnel.
There is no always-on Connection Holder. Tunnel readiness uses health, channel,
command PID and observed terminal-exit evidence; it does not prove an MCP roundtrip.
Owned Godot tunnel launches use `--mcp.max-concurrent-requests 1` to prevent the
installed tunnel client's shared stdio response routing failure. This serializes
Godot requests, including waiting behind a slow tool. External launchers must
apply the same option themselves. See [the transport investigation](docs/reviews/godot-mcp-2026-11-05-transport-addendum.md).

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

Stable publication requires an explicitly approved manual workflow run after all checks. Main-branch builds do not publish a stable release automatically. v0.5.6 retains the update ZIP/checksum names understood by v0.5.5; the previous stable release is retained for upgrade verification.
