using System.Diagnostics;
using ConstantineHub.Core;

namespace ConstantineHub.Adapters.Blender;

internal sealed record BlenderAdapterSnapshot(
    bool BlenderRunning,
    string? BlenderExecutable,
    bool BridgeConnected,
    string BridgeEndpoint,
    BlenderSkillVerification Skills,
    AdapterStatus TunnelStatus);

internal sealed class BlenderAdapter : TunnelProfileAdapterBase
{
    private BlenderSettings _settings;

    internal BlenderAdapter() : this(BlenderSettingsStore.Load())
    {
    }

    private BlenderAdapter(BlenderSettings settings) : base(settings.TunnelProfile)
    {
        _settings = settings;
    }

    public override string Id => "blender";
    public override string DisplayName => "Blender MCP";

    internal BlenderSettings Settings => _settings;

    internal async Task<BlenderAdapterSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var running = IsBlenderRunning();
        var executable = ExecutableLocator.FindBlender(_settings.BlenderExecutable);
        var bridge = await PortProbe.CanConnectAsync(
            _settings.BridgeHost,
            _settings.BridgePort,
            timeoutMs: 450,
            cancellationToken: cancellationToken);
        var skills = BlenderSkillVerifier.Verify(ProfileName);
        var tunnel = await base.GetStatusAsync(cancellationToken);
        return new(
            running,
            executable,
            bridge,
            $"{_settings.BridgeHost}:{_settings.BridgePort}",
            skills,
            tunnel);
    }

    public override async Task<AdapterStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var tunnel = await base.GetStatusAsync(cancellationToken);
        var running = IsBlenderRunning();
        var bridge = await PortProbe.CanConnectAsync(
            _settings.BridgeHost,
            _settings.BridgePort,
            timeoutMs: 450,
            cancellationToken: cancellationToken);
        var skills = BlenderSkillVerifier.Verify(ProfileName);

        var details = tunnel.Details is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(tunnel.Details, StringComparer.OrdinalIgnoreCase);
        details["Blender"] = running ? "running" : "stopped";
        details["Bridge"] = bridge ? $"connected {_settings.BridgeHost}:{_settings.BridgePort}" : "not connected";
        details["Skills"] = skills.Success ? $"{skills.VerifiedCount}/{skills.SourceCount}" : skills.Message;

        if (tunnel.State is AdapterState.Running or AdapterState.External)
        {
            if (!skills.Success)
                return new(Id, DisplayName, AdapterState.Degraded, "Tunnel ready, but Blender skill routing failed verification.", details);
            if (!bridge)
                return new(Id, DisplayName, AdapterState.Degraded, "Tunnel ready; Blender bridge is not listening yet.", details);
        }

        return tunnel with { Id = Id, DisplayName = DisplayName, Details = details };
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _settings = BlenderSettingsStore.Load();
        BlenderSettingsStore.Validate(_settings);

        var skills = BlenderSkillVerifier.Verify(ProfileName);
        if (!skills.Success)
            throw new InvalidOperationException("Blender skill verification failed: " + skills.Message);

        if (_settings.AutoLaunchBlender && !IsBlenderRunning())
            LaunchBlender();

        await base.StartAsync(cancellationToken);

        if (!await PortProbe.CanConnectAsync(
                _settings.BridgeHost,
                _settings.BridgePort,
                timeoutMs: 600,
                cancellationToken: cancellationToken))
        {
            Log($"Tunnel is ready, but Blender bridge {_settings.BridgeHost}:{_settings.BridgePort} is not listening. In Blender open N-panel → BlenderMCP and click Connect.");
        }
    }

    public override async Task<string> DoctorAsync(CancellationToken cancellationToken = default)
    {
        _settings = BlenderSettingsStore.Load();
        var lines = new List<string>();
        try
        {
            lines.Add(await base.DoctorAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            lines.Add("Tunnel Doctor failed: " + ex.Message);
        }

        var blenderRunning = IsBlenderRunning();
        var blenderPath = ExecutableLocator.FindBlender(_settings.BlenderExecutable);
        var bridge = await PortProbe.CanConnectAsync(
            _settings.BridgeHost,
            _settings.BridgePort,
            timeoutMs: 600,
            cancellationToken: cancellationToken);
        var skills = BlenderSkillVerifier.Verify(ProfileName);

        lines.Add(string.Empty);
        lines.Add("BLENDER");
        lines.Add($"Process: {(blenderRunning ? "running" : "not running")}");
        lines.Add("Executable: " + (blenderPath ?? "not found"));
        lines.Add($"Bridge: {(bridge ? "connected" : "not listening")} {_settings.BridgeHost}:{_settings.BridgePort}");
        lines.Add($"Skills: {(skills.Success ? "PASS" : "FAIL")} {skills.VerifiedCount}/{skills.SourceCount} — {skills.Message}");
        return string.Join(Environment.NewLine, lines);
    }

    private void LaunchBlender()
    {
        var executable = ExecutableLocator.FindBlender(_settings.BlenderExecutable)
            ?? throw new FileNotFoundException(
                "Blender executable was not found. Configure BlenderExecutable or install Blender in a discoverable location.");

        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true
        });
        Log("Blender launched: " + executable);
    }

    private static bool IsBlenderRunning()
    {
        try
        {
            return Process.GetProcessesByName("blender").Any(process => !process.HasExited);
        }
        catch
        {
            return false;
        }
    }
}
