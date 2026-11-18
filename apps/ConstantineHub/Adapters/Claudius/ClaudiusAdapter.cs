using ConstantineHub.Adapters.Blender;
using ConstantineHub.Core;

namespace ConstantineHub.Adapters.Claudius;

internal sealed record ClaudiusSnapshot(
    AdapterStatus Status,
    ClaudiusSettings Settings,
    string? SettingsError,
    ClaudeRegistration Registration,
    ClaudiusGatewayState? Gateway,
    bool GatewayLive,
    string? ServerCommand,
    bool ServerFound,
    bool BridgeConnected,
    string BridgeEndpoint);

/// <summary>
/// Claude's own Blender MCP channel. Claude Desktop owns the gateway process; ConHub
/// owns only the switch, so Start/Stop never touch Jarvis tunnels or Blender itself.
/// </summary>
internal sealed class ClaudiusAdapter : IHubAdapter
{
    private readonly Func<BlenderSettings> _blenderSettings;

    internal ClaudiusAdapter(Func<BlenderSettings>? blenderSettings = null)
        => _blenderSettings = blenderSettings ?? BlenderSettingsStore.Load;

    public string Id => "claudius";
    public string DisplayName => "Claudius MCP";
    internal event Action<string>? LogLine;
    internal string Launcher { get; init; } = ClaudeDesktopConfig.LauncherPath;

    public async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => (await GetSnapshotAsync(cancellationToken)).Status;

    internal async Task<ClaudiusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        ClaudiusSettings settings;
        string? settingsError = null;
        try { settings = ClaudiusChannelStore.Load(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            settings = ClaudiusSettings.Default;
            settingsError = ex.Message;
        }

        var blender = _blenderSettings();
        var server = ResolveServerCommand(settings, blender);
        var serverFound = server is not null && File.Exists(server);
        var registration = ClaudeDesktopConfig.Inspect(ClaudeDesktopConfig.Locate(), Launcher, ClaudiusChannelStore.ConfigPath);
        var gateway = ClaudiusChannelStore.LoadState();
        var live = ClaudiusChannelStore.IsLive(gateway, DateTimeOffset.UtcNow);
        var bridge = await PortProbe.CanConnectAsync(blender.BridgeHost, blender.BridgePort, 450, cancellationToken);

        var (state, summary) = settingsError is not null ? (AdapterState.Failed, settingsError)
            : !registration.Registered ? (AdapterState.NotConfigured, registration.Message + " Press Start to register.")
            : !registration.Current ? (AdapterState.NotConfigured, "Claude entry bypasses the channel switch. Press Start to route it through ConHub.")
            : !serverFound ? (AdapterState.NotConfigured, "Blender MCP server command not found.")
            : !settings.Enabled ? (AdapterState.Stopped, "Channel closed · Claude tool calls refused")
            : live ? (AdapterState.Running, "Open · Claude connected")
            : (AdapterState.Running, "Open · waiting for Claude Desktop");

        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Channel"] = settings.Enabled ? "open" : "closed",
            ["Claude config"] = registration.ConfigPath ?? "not found",
            ["Gateway"] = live ? $"pid {gateway!.GatewayPid}" : "not running",
            ["Server"] = server ?? "unresolved"
        };
        return new(new(Id, DisplayName, state, summary, details), settings, settingsError, registration,
            gateway, live, server, serverFound, bridge, $"{blender.BridgeHost}:{blender.BridgePort}");
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        var settings = ClaudiusChannelStore.Load();
        var server = ResolveServerCommand(settings, _blenderSettings())
            ?? throw new InvalidOperationException("Blender MCP server command is unknown. Configure the Blender MCP tunnel profile first.");
        if (!File.Exists(server))
            throw new FileNotFoundException("Blender MCP server command not found.", server);

        EnsureRegistered();
        ClaudiusChannelStore.Save(settings with { Enabled = true, ServerCommand = server });
        Log("Claudius channel opened. Claude tool calls now reach Blender MCP_Con: " + server);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        var settings = ClaudiusChannelStore.Load();
        ClaudiusChannelStore.Save(settings with { Enabled = false });
        Log("Claudius channel closed. Claude stays registered; its tool calls are refused until Start. Jarvis tunnels are untouched.");
        return Task.CompletedTask;
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        await StartAsync(cancellationToken);
    }

    /// <summary>Explicit repair: rewrite the Claude Desktop entry even if the channel stays closed.</summary>
    internal Task RepairRegistrationAsync(CancellationToken cancellationToken = default)
    {
        EnsureRegistered(forceLog: true);
        return Task.CompletedTask;
    }

    public async Task<string> DoctorAsync(CancellationToken cancellationToken = default)
    {
        var s = await GetSnapshotAsync(cancellationToken);
        var lines = new List<string>
        {
            "CLAUDIUS MCP — Claude's own Blender channel",
            $"State: {s.Status.State} — {s.Status.Summary}",
            "",
            "CHANNEL (owned by ConHub)",
            $"Switch: {(s.Settings.Enabled ? "OPEN" : "CLOSED")}  ({ClaudiusChannelStore.ConfigPath})",
            s.SettingsError is null ? "Config: readable" : "Config: UNREADABLE — " + s.SettingsError,
            $"Server: {(s.ServerFound ? "found" : "MISSING")} {s.ServerCommand ?? "(unresolved)"}",
            "",
            "CLAUDE DESKTOP (owns the gateway process)",
            $"Config: {s.Registration.ConfigPath ?? "not found"}",
            $"Entry '{ClaudeDesktopConfig.ServerName}': {s.Registration.Message}",
            $"Launcher: {(File.Exists(Launcher) ? "present" : "MISSING")} {Launcher}",
            s.GatewayLive
                ? $"Gateway: live pid {s.Gateway!.GatewayPid}, server pid {s.Gateway.ChildPid}, client {s.Gateway.Client ?? "unknown"}"
                : "Gateway: not running (Claude Desktop closed, or restart it once after registration)",
            s.Gateway is null ? "Activity: none recorded"
                : $"Activity: {s.Gateway.Calls} forwarded, {s.Gateway.Blocked} refused, last {s.Gateway.LastTool ?? "—"} at {s.Gateway.LastCallUtc?.ToLocalTime():HH:mm:ss}",
            "",
            "BLENDER",
            $"Bridge: {(s.BridgeConnected ? "listening" : "not listening")} {s.BridgeEndpoint}"
        };
        if (s.Registration.BlenderLabExtension)
            lines.Add("Note: Claude Desktop also has the Blender Lab extension. Its scene tools are rejected by the authenticated bridge (REJECTED_BEFORE_EXECUTION); use it for API docs only or disable it.");
        return string.Join(Environment.NewLine, lines);
    }

    private void EnsureRegistered(bool forceLog = false)
    {
        var config = ClaudeDesktopConfig.Locate()
            ?? throw new FileNotFoundException("Claude Desktop config was not found. Open Claude Desktop once, then retry.");
        var backup = ClaudeDesktopConfig.Register(config, Launcher, ClaudiusChannelStore.ConfigPath);
        if (backup is not null)
            Log($"Registered '{ClaudeDesktopConfig.ServerName}' in Claude Desktop ({config}); backup {backup}. Restart Claude Desktop once to load the channel.");
        else if (forceLog)
            Log("Claude Desktop registration is already current: " + config);
    }

    private static string? ResolveServerCommand(ClaudiusSettings settings, BlenderSettings blender)
    {
        try
        {
            return TunnelProfile.CommandPath(TunnelProfile.Load(blender.TunnelProfile).McpCommand);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return string.IsNullOrWhiteSpace(settings.ServerCommand) ? null : settings.ServerCommand;
        }
    }

    private void Log(string message) => LogLine?.Invoke(message);
}
