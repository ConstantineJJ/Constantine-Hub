# NVIDIA App MCP in ConHub.

The optional NVIDIA Utility card reuses ConHub.'s managed tunnel controller and NVIDIA App's installed `NvAppMcpServer.exe` stdio relay. It leaves NVIDIA App and all externally owned processes running. It does not bind NVIDIA's HTTP port to any public interface.

Local discovery reads `%LOCALAPPDATA%\NVIDIA Corporation\NvAppMcpServer\server.json`, accepting only `http://127.0.0.1:<port>/mcp`, or uses `http://127.0.0.1:13508/mcp`. The bounded read-only probe initializes MCP, verifies the server identifies itself as `nvidia-app`, enumerates tools, and closes its session. A listening TCP port alone is insufficient. Results are cached for 15 seconds. Doctor refreshes them immediately. Local HTTP readiness and tunnel readiness are reported separately: the tunnel uses NVIDIA's own named-pipe relay.

## One-time private tunnel setup

Enable **AI Agents and App Connections** in NVIDIA App. Create a separate private MCP tunnel in OpenAI Platform; keep the existing Godot, Blender and Local Files profiles. With the installed authenticated tunnel client, replace the two placeholders:

```powershell
$tc = 'C:\PATH\TO\tunnel-client.exe'
$relayCommand = '"C:/Program Files/NVIDIA Corporation/NVIDIA App/McpServer/NvAppMcpServer.exe"'
& $tc init --sample sample_mcp_stdio_local --profile constantine-nvidia `
  --tunnel-id tunnel_YOUR_NVIDIA_ID --health-listen-addr 127.0.0.1:8083 `
  --mcp-command $relayCommand
& $tc doctor --profile constantine-nvidia --explain
```

Use an unused loopback health port. ConHub. reads the existing `CONTROL_PLANE_API_KEY` environment variable; never paste it into logs. Start/Stop/Restart in Utility control only the Hub-owned tunnel. Start All skips NVIDIA when its dedicated profile is absent. Doctor remains available without a configured tunnel. Stopping NVIDIA App itself reports local MCP unavailable; restarting the relay never terminates unrelated NVIDIA processes.

The inner double quotes in `$relayCommand` are part of the command passed to tunnel-client, whose command parser needs them for `Program Files`. PowerShell argument quoting alone does not preserve that executable boundary. Native `init` serializes the command as a double-quoted YAML scalar with escaped inner quotes; ConHub. decodes that scalar and validates the exact installed executable without accepting additional arguments. Existing plain and single-quoted profile values remain supported.

Connect the private tunnel in the MCP client, verify `nvapp_*` tools are discovered, and call a read-only tool such as `nvapp_client_get_driver_status`. Successful local discovery is not evidence that a newly configured remote tunnel works; verify that separately.

The implementation retains the `constantine-nvidia` profile and installed relay approach from PR #18. Its original TCP-only readiness check was replaced by MCP initialization and tool discovery. No driver, game, overlay or NVIDIA settings are changed by ConHub. diagnostics.
