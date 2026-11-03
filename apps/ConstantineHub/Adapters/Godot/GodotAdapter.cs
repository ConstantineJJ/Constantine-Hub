using System.Diagnostics;
using ConstantineHub.Core;

namespace ConstantineHub.Adapters.Godot;

internal sealed record GodotAdapterSnapshot(
    bool Configured,
    string ProjectRoot,
    GodotPluginStatus Plugin,
    bool GodotRunning,
    string? GodotExecutable,
    bool EditorBridgeConnected,
    bool RuntimeBridgeConnected,
    string SkillsSummary,
    bool SkillsOk,
    AdapterStatus TunnelStatus,
    string? EditorBridgeIssue = null,
    string? RuntimeBridgeIssue = null);

internal sealed class GodotAdapter : TunnelProfileAdapterBase
{
    private GodotSettings _settings;
    private readonly SemaphoreSlim _projectSwitch = new(1, 1);

    internal GodotAdapter() : this(GodotSettingsStore.Load()) { }
    private GodotAdapter(GodotSettings settings) : base(settings.TunnelProfile) => _settings = settings;

    public override string Id => "godot";
    public override string DisplayName => "Godot MCP";
    internal GodotSettings Settings => _settings;

    internal async Task<GodotAdapterSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        _settings = GodotSettingsStore.Load();
        var plugin = GodotPluginInstaller.Inspect(_settings);
        var skills = VerifySkills(_settings);
        var tunnel = await base.GetStatusAsync(cancellationToken);
        if (GodotSettingsStore.ProjectIssue(_settings) is { } issue)
            return new(false, _settings.ProjectRoot, plugin, false, ExecutableLocator.FindGodot(_settings.GodotExecutable),
                false, false, skills.Message, skills.Success, tunnel, issue, issue);
        var editor = await GodotBridgeProbe.ProbeAsync(_settings.EditorHost, _settings.EditorPort, _settings.ProjectRoot, cancellationToken);
        var runtime = await GodotBridgeProbe.ProbeAsync(_settings.RuntimeHost, _settings.RuntimePort, _settings.ProjectRoot, cancellationToken);
        return new(true, _settings.ProjectRoot, plugin, editor.Connected, ExecutableLocator.FindGodot(_settings.GodotExecutable), editor.Connected, runtime.Connected, skills.Message, skills.Success, tunnel,
            editor.Connected ? null : editor.Message, runtime.Connected ? null : runtime.Message);
    }

    internal async Task SelectProjectAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        await _projectSwitch.WaitAsync(cancellationToken);
        try
        {
            var previous = GodotSettingsStore.Load();
            if (GodotProjectRegistry.SameProject(previous.ProjectRoot, projectRoot)) return;
            var candidate = previous with { ProjectRoot = Path.GetFullPath(projectRoot) };
            GodotSettingsStore.Validate(candidate);
            var current = await TunnelRuntimeProbe.ProbeAsync(ProfileName, cancellationToken);
            if (!OwnsRunningTunnel && current.State is TunnelRuntimeState.RunningExpected or TunnelRuntimeState.McpUnavailable)
                throw new InvalidOperationException("Stop the Godot tunnel in the launcher that owns it before switching projects. The current tunnel is managed outside Hub.");
            if (current.State == TunnelRuntimeState.Occupied)
                throw new InvalidOperationException("The Godot tunnel health port is occupied. Stop its current owner before switching projects.");
            GodotProjectRegistry.Register(candidate.ProjectRoot);
            var restart = OwnsRunningTunnel;
            if (restart) await base.StopAsync(cancellationToken);
            try
            {
                GodotSettingsStore.Save(candidate);
                _settings = candidate;
                if (restart) await StartCoreAsync(cancellationToken);
                Log("Selected project: " + candidate.ProjectRoot);
            }
            catch
            {
                await base.StopAsync(CancellationToken.None);
                GodotSettingsStore.Save(previous);
                _settings = previous;
                if (restart)
                {
                    try { await StartCoreAsync(CancellationToken.None); }
                    catch (Exception restoreError) { Log("Previous project restored; tunnel restart failed: " + restoreError.Message); }
                }
                throw;
            }
        }
        finally { _projectSwitch.Release(); }
    }

    public override async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var details = snapshot.TunnelStatus.Details is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(snapshot.TunnelStatus.Details, StringComparer.OrdinalIgnoreCase);
        details["Project"] = snapshot.ProjectRoot;
        details["Plugin"] = snapshot.Plugin.Message;
        details["Godot"] = snapshot.GodotRunning ? "running" : "stopped";
        details["Editor bridge"] = snapshot.EditorBridgeConnected ? $"connected {_settings.EditorHost}:{_settings.EditorPort}" : "not connected";
        details["Runtime bridge"] = snapshot.RuntimeBridgeConnected ? $"connected {_settings.RuntimeHost}:{_settings.RuntimePort}" : "not connected";
        details["Skills"] = snapshot.SkillsSummary;

        if (!snapshot.Configured && snapshot.TunnelStatus.State is AdapterState.Stopped or AdapterState.NotConfigured)
            return new(Id, DisplayName, AdapterState.NotConfigured, snapshot.Plugin.Message, details);

        if (snapshot.TunnelStatus.State is AdapterState.Running or AdapterState.External)
        {
            if (!snapshot.Plugin.Installed || !snapshot.Plugin.Enabled)
                return new(Id, DisplayName, AdapterState.Degraded, "Tunnel ready, but the Godot project plugin is missing or disabled.", details);
            if (!snapshot.SkillsOk)
                return new(Id, DisplayName, AdapterState.Degraded, "Tunnel ready, but canonical Godot skills/contracts failed verification.", details);
            if (!snapshot.EditorBridgeConnected)
                return new(Id, DisplayName, AdapterState.Degraded, "Tunnel ready; Godot editor bridge is not connected.", details);
        }

        return snapshot.TunnelStatus with { Id = Id, DisplayName = DisplayName, Details = details };
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
        => await ExclusiveAsync(StartCoreAsync, cancellationToken);

    public override async Task StopAsync(CancellationToken cancellationToken = default)
        => await ExclusiveAsync(base.StopAsync, cancellationToken);

    public override async Task RestartAsync(CancellationToken cancellationToken = default)
        => await ExclusiveAsync(RestartCoreAsync, cancellationToken);

    private async Task ExclusiveAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        await _projectSwitch.WaitAsync(token);
        try { token.ThrowIfCancellationRequested(); await action(token); }
        finally { _projectSwitch.Release(); }
    }

    private async Task EnsureRecoveryOwnershipAsync(CancellationToken token)
    {
        var current = await TunnelRuntimeProbe.ProbeAsync(ProfileName, token);
        if (!OwnsRunningTunnel && current.State is TunnelRuntimeState.RunningExpected or TunnelRuntimeState.McpUnavailable)
            throw new ExternalTunnelControlException(DisplayName);
        if (current.State == TunnelRuntimeState.Occupied) throw new InvalidOperationException(current.Message);
    }

    private async Task RestartCoreAsync(CancellationToken token)
    {
        await EnsureRecoveryOwnershipAsync(token);
        await base.StopAsync(token);
        await StartCoreAsync(token);
    }

    internal Task RecoverAsync(bool alternate, CancellationToken token)
        => ExclusiveAsync(async ct =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            ct = deadline.Token;
            // Recheck after waiting for a main-module Restart/project switch.
            var snapshot = await GetSnapshotAsync(ct);
            if (snapshot.EditorBridgeConnected && snapshot.TunnelStatus.State is AdapterState.Running or AdapterState.External) return;
            if (!alternate) { await RestartCoreAsync(ct); return; }
            await EnsureRecoveryOwnershipAsync(ct);
            await base.StopAsync(ct);
            Log(await DoctorCoreAsync(ct));
            ct.ThrowIfCancellationRequested();
            await StartCoreAsync(ct);
        }, token);

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _settings = GodotSettingsStore.Load();
        GodotSettingsStore.Validate(_settings);
        var skills = VerifySkills(_settings);
        if (!skills.Success)
            throw new InvalidOperationException("Godot canonical skills/contracts failed verification: " + skills.Message);

        await EnsurePythonAdapterAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var pluginBefore = GodotPluginInstaller.Inspect(_settings);
        var pluginAfter = GodotPluginInstaller.InstallOrUpdate(_settings);
        if (!pluginBefore.Installed || !pluginBefore.Enabled)
            Log("Godot project plugin installed/enabled. If the editor was already open, restart/reload the project so the editor bridge can activate.");
        if (!pluginAfter.Installed || !pluginAfter.Enabled)
            throw new InvalidOperationException(pluginAfter.Message);

        await EnsureTunnelProfileAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var selectedEditor = await GodotBridgeProbe.ProbeAsync(_settings.EditorHost, _settings.EditorPort, _settings.ProjectRoot, cancellationToken);
        if (_settings.AutoLaunchGodot && !selectedEditor.Connected && !selectedEditor.Message.Contains("another project", StringComparison.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            LaunchGodot();
        }
        await base.StartAsync(cancellationToken);

        var readyEditor = await GodotBridgeProbe.ProbeAsync(_settings.EditorHost, _settings.EditorPort, _settings.ProjectRoot, cancellationToken);
        if (!readyEditor.Connected)
            Log("Tunnel is ready; " + readyEditor.Message + " Reload the selected project or enable its Constantine MCP editor plugin.");
    }

    internal GodotPluginStatus InstallOrUpdatePlugin()
    {
        _settings = GodotSettingsStore.Load();
        GodotSettingsStore.Validate(_settings);
        return GodotPluginInstaller.InstallOrUpdate(_settings);
    }

    public override async Task<string> DoctorAsync(CancellationToken cancellationToken = default)
    {
        await _projectSwitch.WaitAsync(cancellationToken);
        try { return await DoctorCoreAsync(cancellationToken); }
        finally { _projectSwitch.Release(); }
    }

    private async Task<string> DoctorCoreAsync(CancellationToken cancellationToken)
    {
        _settings = GodotSettingsStore.Load();
        if (GodotSettingsStore.ProjectIssue(_settings) is { } issue) return issue;
        var lines = new List<string>();
        try
        {
            await EnsureTunnelProfileAsync(cancellationToken);
            lines.Add(await base.DoctorAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            lines.Add("Tunnel Doctor failed: " + ex.Message);
        }

        var plugin = GodotPluginInstaller.Inspect(_settings);
        var editor = await GodotBridgeProbe.ProbeAsync(_settings.EditorHost, _settings.EditorPort, _settings.ProjectRoot, cancellationToken);
        var runtime = await GodotBridgeProbe.ProbeAsync(_settings.RuntimeHost, _settings.RuntimePort, _settings.ProjectRoot, cancellationToken);
        var skills = VerifySkills(_settings);
        var executable = ExecutableLocator.FindGodot(_settings.GodotExecutable);

        lines.Add(string.Empty);
        lines.Add("GODOT");
        lines.Add("Project: " + _settings.ProjectRoot);
        lines.Add("Selected project editor: " + (editor.Connected ? "connected" : "not connected"));
        lines.Add("Executable: " + (executable ?? "not found"));
        lines.Add("Plugin: " + plugin.Message);
        lines.Add("Editor bridge: " + editor.Message);
        lines.Add("Runtime bridge: " + runtime.Message);
        lines.Add($"Skills/contracts: {(skills.Success ? "PASS" : "FAIL")} — {skills.Message}");
        return string.Join(Environment.NewLine, lines);
    }

    private async Task EnsurePythonAdapterAsync(CancellationToken cancellationToken)
    {
        var root = GodotSettingsStore.RepositoryAdapterRoot;
        var python = Path.Combine(root, ".venv", "Scripts", "python.exe");
        if (File.Exists(python)) return;

        var create = await ProcessCapture.RunAsync("py", ["-3", "-m", "venv", Path.Combine(root, ".venv")], cancellationToken);
        if (create.ExitCode != 0 || !File.Exists(python))
            throw new InvalidOperationException("Could not create Godot MCP Python venv: " + create.Output);
        var install = await ProcessCapture.RunAsync(python, ["-m", "pip", "install", "-e", root], cancellationToken);
        if (install.ExitCode != 0)
            throw new InvalidOperationException("Could not install Godot MCP Python package: " + install.Output);
        Log("Godot MCP Python environment provisioned.");
    }

    private async Task EnsureTunnelProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            var existing = TunnelProfile.Load(_settings.TunnelProfile);
            var expectedCommand = Path.GetFullPath(GodotSettingsStore.McpCommand);
            var configuredCommand = Path.GetFullPath(Environment.ExpandEnvironmentVariables(existing.McpCommand).Replace('/', Path.DirectorySeparatorChar));
            if (!string.Equals(existing.TunnelId, _settings.TunnelId, StringComparison.Ordinal) ||
                existing.HealthPort != _settings.HealthPort ||
                !string.Equals(configuredCommand, expectedCommand, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Existing tunnel profile '{_settings.TunnelProfile}' does not match ConHub. Godot settings. Remove or rename the stale profile before Setup.");
            return;
        }
        catch (FileNotFoundException) { }

        var tunnel = ExecutableLocator.FindTunnelClient() ?? throw new FileNotFoundException("tunnel-client.exe was not found.");
        var apiKey = Environment.GetEnvironmentVariable("CONTROL_PLANE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("CONTROL_PLANE_API_KEY is missing.");

        var command = Path.GetFullPath(GodotSettingsStore.McpCommand).Replace('\\', '/');
        if (!File.Exists(Path.GetFullPath(GodotSettingsStore.McpCommand)))
            throw new FileNotFoundException("Godot MCP wrapper was not found.", GodotSettingsStore.McpCommand);

        var result = await ProcessCapture.RunAsync(tunnel,
        [
            "init", "--sample", "sample_mcp_stdio_local",
            "--profile", _settings.TunnelProfile,
            "--tunnel-id", _settings.TunnelId,
            "--mcp-command", command,
            "--health-listen-addr", $"127.0.0.1:{_settings.HealthPort}"
        ], cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Godot tunnel profile bootstrap failed: " + result.Output);
        Log($"Created tunnel profile '{_settings.TunnelProfile}' for {_settings.TunnelId}.");
    }

    private void LaunchGodot()
    {
        var executable = ExecutableLocator.FindGodot(_settings.GodotExecutable)
            ?? throw new FileNotFoundException("Godot executable was not found. Configure godot_executable in Godot settings.");
        var info = new ProcessStartInfo { FileName = executable, UseShellExecute = true };
        info.ArgumentList.Add("--editor");
        info.ArgumentList.Add("--path");
        info.ArgumentList.Add(_settings.ProjectRoot);
        Process.Start(info);
        Log("Godot launched: " + executable);
    }

    private static (bool Success, string Message) VerifySkills(GodotSettings settings)
    {
        try
        {
            var root = CanonicalKnowledge.GodotRoot(settings);
            var required = new[]
            {
                Path.Combine(root, "docs", "foundation.md"),
                Path.Combine(root, "skills", "godot-project", "SKILL.md"),
                Path.Combine(root, "skills", "godot-asset-integration", "SKILL.md"),
                Path.Combine(root, "skills", "verification", "SKILL.md")
            };
            var missing = required.Where(path => !File.Exists(path) || new FileInfo(path).Length == 0).ToArray();
            return missing.Length == 0
                ? (true, "Canonical Tools_C foundation + godot-project + godot-asset-integration + verification found.")
                : (false, "Missing: " + string.Join(", ", missing));
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
