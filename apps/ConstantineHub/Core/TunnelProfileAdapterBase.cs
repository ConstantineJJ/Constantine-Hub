namespace ConstantineHub.Core;

internal abstract class TunnelProfileAdapterBase : IHubAdapter, IDisposable
{
    private readonly ManagedProcess _ownedTunnel;

    protected TunnelProfileAdapterBase(string profileName)
    {
        ProfileName = profileName;
        _ownedTunnel = new ManagedProcess(Log);
    }

    protected string ProfileName { get; }
    protected bool OwnsRunningTunnel => _ownedTunnel.IsRunning;
    protected int? OwnedTunnelPid => _ownedTunnel.ProcessId;

    public abstract string Id { get; }
    public abstract string DisplayName { get; }

    internal event Action<string>? LogLine;

    protected void Log(string message)
        => LogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {DisplayName}: {message}");

    public virtual async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var runtime = await TunnelRuntimeProbe.ProbeAsync(ProfileName, cancellationToken);
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Profile"] = ProfileName,
            ["Owned PID"] = OwnedTunnelPid?.ToString() ?? "—"
        };
        if (runtime.Profile is not null)
        {
            details["Tunnel"] = runtime.Profile.TunnelId;
            details["Health"] = runtime.Profile.ListenAddress;
            details["MCP"] = runtime.Profile.McpCommand;
        }

        foreach (var pair in await GetAdditionalDetailsAsync(cancellationToken))
            details[pair.Key] = pair.Value;

        return runtime.State switch
        {
            TunnelRuntimeState.InvalidProfile => new(
                Id, DisplayName, AdapterState.NotConfigured, runtime.Message, details),
            TunnelRuntimeState.Stopped when _ownedTunnel.IsRunning => new(
                Id, DisplayName, AdapterState.Starting, "Owned tunnel process is starting.", details),
            TunnelRuntimeState.Stopped => new(
                Id, DisplayName, AdapterState.Stopped, "Tunnel stopped.", details),
            TunnelRuntimeState.RunningExpected when _ownedTunnel.IsRunning => new(
                Id, DisplayName, AdapterState.Running, "Tunnel ready (Hub-owned).", details),
            TunnelRuntimeState.RunningExpected => new(
                Id, DisplayName, AdapterState.External, "Tunnel ready (external/adopted).", details),
            TunnelRuntimeState.Occupied => new(
                Id, DisplayName, AdapterState.Degraded, runtime.Message, details),
            _ => new(Id, DisplayName, AdapterState.Failed, runtime.Message, details)
        };
    }

    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var initial = await TunnelRuntimeProbe.ProbeAsync(ProfileName, cancellationToken);
        if (initial.State == TunnelRuntimeState.RunningExpected)
        {
            Log(_ownedTunnel.IsRunning
                ? "Tunnel is already ready and Hub-owned."
                : "Compatible external tunnel detected; adopting status without taking process ownership.");
            return;
        }

        if (initial.State == TunnelRuntimeState.InvalidProfile)
            throw new InvalidOperationException(initial.Message);
        if (initial.State == TunnelRuntimeState.Occupied)
            throw new InvalidOperationException(initial.Message);
        if (_ownedTunnel.IsRunning)
        {
            Log("Owned tunnel process is already running; waiting for readiness.");
        }
        else
        {
            var apiKey = Environment.GetEnvironmentVariable("CONTROL_PLANE_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException(
                    "CONTROL_PLANE_API_KEY is missing. Set it in Windows and restart Constantine Hub.");

            var executable = ExecutableLocator.FindTunnelClient()
                ?? throw new FileNotFoundException(
                    "tunnel-client.exe was not found. Set CONSTANTINE_TUNNEL_CLIENT or install it in the known local path.");

            if (!_ownedTunnel.Start(executable, ["run", "--profile", ProfileName]))
                throw new InvalidOperationException("tunnel-client process could not be started.");
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        TunnelRuntimeStatus? last = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(350, cancellationToken);
            last = await TunnelRuntimeProbe.ProbeAsync(ProfileName, cancellationToken);
            if (last.State == TunnelRuntimeState.RunningExpected)
            {
                Log("Tunnel reached ready state.");
                return;
            }

            if (!_ownedTunnel.IsRunning)
                throw new InvalidOperationException(
                    "tunnel-client exited before readiness. Last probe: " + last.Message);
            if (last.State == TunnelRuntimeState.InvalidProfile)
                throw new InvalidOperationException(last.Message);
        }

        throw new TimeoutException(
            "tunnel-client is still running but readiness was not confirmed within 12 seconds. Last probe: " +
            (last?.Message ?? "no result"));
    }

    public virtual Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_ownedTunnel.IsRunning)
        {
            _ownedTunnel.Stop();
            Log("Stopped Hub-owned tunnel.");
        }
        else
        {
            Log("No Hub-owned tunnel to stop. Any compatible external tunnel is left untouched.");
        }
        return Task.CompletedTask;
    }

    public virtual async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        if (_ownedTunnel.IsRunning)
            await StopAsync(cancellationToken);
        else
        {
            var current = await TunnelRuntimeProbe.ProbeAsync(ProfileName, cancellationToken);
            if (current.State == TunnelRuntimeState.RunningExpected)
                throw new InvalidOperationException(
                    "The active tunnel is external/adopted. Hub will not stop it; restart it from its owner.");
        }

        await StartAsync(cancellationToken);
    }

    public virtual async Task<string> DoctorAsync(CancellationToken cancellationToken = default)
    {
        var executable = ExecutableLocator.FindTunnelClient()
            ?? throw new FileNotFoundException("tunnel-client.exe was not found.");

        var current = await TunnelRuntimeProbe.ProbeAsync(ProfileName, cancellationToken);
        var arguments = new List<string> { "doctor", "--profile", ProfileName, "--explain" };
        if (current.State == TunnelRuntimeState.RunningExpected)
            arguments.AddRange(["--health.listen-addr", "127.0.0.1:0"]);

        var result = await ProcessCapture.RunAsync(executable, arguments, cancellationToken);
        return $"Exit code: {result.ExitCode}{Environment.NewLine}{result.Output}";
    }

    protected virtual Task<IReadOnlyDictionary<string, string>> GetAdditionalDetailsAsync(
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>());

    public void Dispose() => _ownedTunnel.Dispose();
}
