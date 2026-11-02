# NVIDIA App MCP in Constantine Hub

This is an optional fourth managed adapter in Constantine Hub. It reuses Hub's existing `TunnelProfileAdapterBase` and the authenticated `tunnel-client`; it does **not** expose NVIDIA's local HTTP port as an unauthenticated public endpoint.

## What it manages

- Dedicated tunnel-client profile: `constantine-nvidia`
- NVIDIA App's installed stdio relay: `%ProgramFiles%\NVIDIA Corporation\NVIDIA App\McpServer\NvAppMcpServer.exe`
- Local status (read-only): `%LOCALAPPDATA%\NVIDIA Corporation\NvAppMcpServer\server.json` and loopback TCP probe
- Hub controls only the tunnel-client process it started. It does not start, stop, or modify NVIDIA App and does not touch existing Blender, Godot, or Local Files tunnels.

The stdio relay talks to the NVIDIA App MCP service over a per-user named pipe. **NVIDIA App must have AI Agents and App Connections enabled.** The relay is not a replacement for that persistent service.

## One-time secure bootstrap (Windows PowerShell)

1. In OpenAI Platform, create a **separate private MCP tunnel** for NVIDIA App. Record only its `tunnel_id`. Keep credentials out of the repository and transcripts.
2. Confirm that port `127.0.0.1:8083` is unused (the sample below reserves it for this adapter).
3. Use the `tunnel-client.exe` already installed for Constantine Hub. Example (replace the executable path and tunnel ID):

```powershell
$tc = "C:\PATH\TO\tunnel-client.exe"
& $tc init `
  --sample sample_mcp_stdio_local `
  --profile constantine-nvidia `
  --tunnel-id tunnel_YOUR_NVIDIA_ID `
  --health-listen-addr 127.0.0.1:8083 `
  --mcp-command "C:/Program Files/NVIDIA Corporation/NVIDIA App/McpServer/NvAppMcpServer.exe"

& $tc doctor --profile constantine-nvidia --explain
```

Do not paste or log `CONTROL_PLANE_API_KEY`. Hub reads the already configured Windows environment variable at runtime, as for the other adapters.

4. Restart/rebuild Constantine Hub to load the new adapter. On its NVIDIA App MCP card, press **Start**, then **Doctor**.
5. Connect the newly provisioned secure tunnel to ChatGPT as an MCP connector/plugin. A successful local health probe **alone** does not mean ChatGPT can call NVIDIA tools. Verify discovery of `nvapp_*` tools in a fresh chat.

## Diagnostics

- Hub's NVIDIA card shows the current local HTTP endpoint discovered from NVIDIA's `server.json` if available; it falls back to `127.0.0.1:13508` only as a probe.
- HTTP status is **informational**: the tunnel uses stdio, which can still work when NVIDIA's HTTP listener has failed but the named pipe remains available.
- If the Hub profile is absent, it shows **Not configured**; **Start All** skips NVIDIA rather than logging an error.
- If the stdio executable is missing, update NVIDIA App rather than copying random binaries.
- If `tunnel-client doctor` fails, check the dedicated `tunnel_id`, matching profile, health port, environment key, and App MCP toggle. Do not change Godot or Blender profiles to fix NVIDIA.
- No API keys or NVIDIA state mutations are stored in this adapter.

## Verification before release

Run on the actual Windows host:

```powershell
dotnet build "E:\MyCreations\Constantine-Hub\apps\ConstantineHub\ConstantineHub.csproj" -c Release
```

Then validate Local Files, Blender and Godot cards have unchanged behavior, NVIDIA Start/Stop/Doctor, and that the ChatGPT plugin exposes `nvapp_*` tools through the **new** secure tunnel. The source was edited through a file-only bridge; Windows compilation/runtime testing must be completed locally.
