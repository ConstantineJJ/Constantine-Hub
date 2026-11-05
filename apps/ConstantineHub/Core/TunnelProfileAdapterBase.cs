using System.Text.Json;

namespace ConstantineHub.Core;

internal abstract class TunnelProfileAdapterBase : IHubAdapter, IDisposable
{
    private readonly ManagedProcess _ownedTunnel;
    private int _stdioExited;

    protected TunnelProfileAdapterBase(string profileName)
    {
        ProfileName = profileName;
        _ownedTunnel = new ManagedProcess(ObserveTunnelLog);
        _ownedTunnel.Started += () => Interlocked.Exchange(ref _stdioExited, 0);
    }

    protected string ProfileName { get; }
    protected virtual IReadOnlyList<string> TunnelRunArguments => ["run", "--profile", ProfileName];
    protected bool OwnsRunningTunnel => _ownedTunnel.IsRunning;
    protected int? OwnedTunnelPid => _ownedTunnel.ProcessId;

    public abstract string Id { get; }
    public abstract string DisplayName { get; }

    internal event Action<string>? LogLine;

    protected void Log(string message)
        => LogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {DisplayName}: {message}");

    private void ObserveTunnelLog(string message)
    {
        // Only the current process's structured terminal event is authoritative.
        // Tool errors and transient network failures must not poison readiness.
        if (message.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("component", out var component) && component.ValueKind == JsonValueKind.String && component.ValueEquals("mcpclient") &&
                    root.TryGetProperty("transport", out var transport) && transport.ValueKind == JsonValueKind.String && transport.ValueEquals("stdio") &&
                    root.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String &&
                    msg.ValueEquals("stdio MCP command exited; tunnel-client remains running in degraded mode"))
                    Interlocked.Exchange(ref _stdioExited, 1);
            }
            catch (JsonException) { }
        }
        Log(message);
    }

    private async Task<TunnelRuntimeStatus> ProbeRuntimeAsync(CancellationToken token)
    {
        var runtime = await TunnelRuntimeProbe.ProbeAsync(ProfileName, token);
        return runtime.State == TunnelRuntimeState.RunningExpected && _ownedTunnel.IsRunning && Volatile.Read(ref _stdioExited) != 0
            ? runtime with { State = TunnelRuntimeState.McpUnavailable,
                Message = "Tunnel is running, but its current stdio MCP command reported exit. Restart the tunnel to restore MCP calls." }
            : runtime;
    }

    public virtual async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var runtime = await ProbeRuntimeAsync(cancellationToken);
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
                Id, DisplayName, AdapterState.External, "Tunnel ready (managed outside Hub).", details),
            TunnelRuntimeState.Occupied or TunnelRuntimeState.McpUnavailable => new(
                Id, DisplayName, AdapterState.Degraded, runtime.Message, details),
            _ => new(Id, DisplayName, AdapterState.Failed, runtime.Message, details)
        };
    }

    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var initial = await ProbeRuntimeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
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
        if (initial.State == TunnelRuntimeState.McpUnavailable)
        {
            if (!_ownedTunnel.IsRunning) throw new ExternalTunnelControlException(DisplayName);
            Log("MCP server exited or became unavailable; restarting the Hub-owned tunnel.");
            await StopOwnedTunnelAsync(cancellationToken);
        }
        if (_ownedTunnel.IsRunning)
        {
            Log("Owned tunnel process is already running; waiting for readiness.");
        }
        else
        {
            var apiKey = Environment.GetEnvironmentVariable("CONTROL_PLANE_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException(
                    "CONTROL_PLANE_API_KEY is missing. Set it in Windows and restart ConHub..");

            var executable = ExecutableLocator.FindTunnelClient()
                ?? throw new FileNotFoundException(
                    "tunnel-client.exe was not found. Set CONSTANTINE_TUNNEL_CLIENT or install it in the known local path.");

            cancellationToken.ThrowIfCancellationRequested();
            if (!_ownedTunnel.Start(executable, TunnelRunArguments))
                throw new InvalidOperationException("tunnel-client process could not be started.");
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        TunnelRuntimeStatus? last = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(350, cancellationToken);
            last = await ProbeRuntimeAsync(cancellationToken);
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
        => StopOwnedTunnelAsync(cancellationToken);

    private Task StopOwnedTunnelAsync(CancellationToken cancellationToken)
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
            if (current.State is TunnelRuntimeState.RunningExpected or TunnelRuntimeState.McpUnavailable)
                throw new ExternalTunnelControlException(DisplayName);
        }

        await StartAsync(cancellationToken);
    }

    public virtual async Task<string> DoctorAsync(CancellationToken cancellationToken = default)
    {
        var executable = ExecutableLocator.FindTunnelClient()
            ?? throw new FileNotFoundException("tunnel-client.exe was not found.");

        var current = await TunnelRuntimeProbe.ProbeAsync(ProfileName, cancellationToken);
        var arguments = new List<string> { "doctor", "--profile", ProfileName, "--explain" };
        if (current.State is TunnelRuntimeState.RunningExpected or TunnelRuntimeState.McpUnavailable)
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
