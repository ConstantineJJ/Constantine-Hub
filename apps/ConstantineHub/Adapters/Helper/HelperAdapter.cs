using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using ConstantineHub.Core;

namespace ConstantineHub.Adapters.Helper;

internal sealed class HelperAdapter : IHubAdapter, IDisposable
{
    private Process? process;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private string? ownedInstance;
    private string? ownedConfig;
    private string logToken = "";
    internal event Action<string>? LogLine;
    public string Id => "mcp-helper";
    public string DisplayName => "MCP Helper";
    internal bool OwnsProcess => process is { HasExited: false };
    internal int? OwnedPid => OwnsProcess ? process!.Id : null;

    public async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = HelperSettings.Load();
            if (string.IsNullOrWhiteSpace(settings.WorkspaceRoot)) return new(Id, DisplayName, AdapterState.NotConfigured, "Choose a workspace");
            settings.Validate();
            if (!await PortOpen(settings.Port, cancellationToken)) return new(Id, DisplayName, OwnsProcess ? AdapterState.Starting : AdapterState.Stopped, "Stopped");
            var identity = await HelperConnection.ProbeAsync(settings, cancellationToken);
            if (OwnsProcess && identity.InstanceId != ownedInstance)
                return new(Id, DisplayName, AdapterState.Degraded, "Endpoint does not match the owned Helper instance");
            return new(Id, DisplayName, OwnsProcess ? AdapterState.Running : AdapterState.External,
                $"v{identity.Version} • MCP ready ({(OwnsProcess ? "Hub-owned" : "external")})",
                new Dictionary<string, string> { ["Workspace"] = identity.WorkspaceRoot, ["Endpoint"] = $"http://127.0.0.1:{settings.Port}/mcp" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new(Id, DisplayName, AdapterState.Degraded, "Helper configuration or MCP probe failed; run Doctor"); }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        cancellationToken = linked.Token;
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (OwnsProcess) return;
            StopOwned();
            var settings = HelperSettings.Load();
            settings.Validate();
            if (await PortOpen(settings.Port, cancellationToken))
                throw new InvalidOperationException("Helper port is already occupied. The external process was left untouched.");
            HelperPackage.Verify(settings.ExecutablePath);
            cancellationToken.ThrowIfCancellationRequested();
            ownedInstance = Guid.NewGuid().ToString("N");
            ownedConfig = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(HelperSettings.ConfigPath))!, "helper-service-" + ownedInstance + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(ownedConfig)!);
            File.WriteAllText(ownedConfig, JsonSerializer.Serialize(new { settings.WorkspaceRoot, settings.Port, settings.Token, InstanceId = ownedInstance }));
            logToken = settings.Token;
            process?.Dispose();
            var start = new ProcessStartInfo(settings.ExecutablePath)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(settings.ExecutablePath)!
            };
            start.ArgumentList.Add("serve"); start.ArgumentList.Add("--config"); start.ArgumentList.Add(ownedConfig);
            var child = new Process { StartInfo = start };
            child.OutputDataReceived += (_, e) => Log(e.Data);
            child.ErrorDataReceived += (_, e) => Log(e.Data);
            try { if (!child.Start()) throw new InvalidOperationException("Helper could not start."); }
            catch { child.Dispose(); throw; }
            process = child;
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            for (var attempt = 0; attempt < 60; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException("Helper exited before becoming ready.");
                if (await PortOpen(settings.Port, cancellationToken))
                {
                    var identity = await HelperConnection.ProbeAsync(settings, cancellationToken);
                    if (identity.InstanceId != ownedInstance) throw new InvalidOperationException("Helper instance mismatch.");
                    Log("MCP Helper ready (Hub-owned).");
                    return;
                }
                await Task.Delay(150, cancellationToken);
            }
            throw new TimeoutException("Helper did not become ready.");
        }
        catch { StopOwned(); throw; }
        finally { lifecycle.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await lifecycle.WaitAsync(cancellationToken);
        try { StopOwned(); }
        finally { lifecycle.Release(); }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        if (!OwnsProcess && (await GetStatusAsync(cancellationToken)).State == AdapterState.External)
            throw new InvalidOperationException("External Helper must be restarted in its own launcher.");
        await StopAsync(cancellationToken);
        await StartAsync(cancellationToken);
    }

    public async Task<string> DoctorAsync(CancellationToken cancellationToken = default)
    {
        var settings = HelperSettings.Load();
        settings.Validate();
        var package = HelperPackage.Verify(settings.ExecutablePath);
        var state = await GetStatusAsync(cancellationToken);
        return $"Package {package}: hashes verified.\nWorkspace: {settings.WorkspaceRoot}\nEndpoint: http://127.0.0.1:{settings.Port}/mcp\nMCP: {state.State} — {state.Summary}\nBackend: Roslyn, partial disk-only analysis; no project code execution.\nApply: unavailable.";
    }

    private void StopOwned()
    {
        if (OwnsProcess)
        {
            process!.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5000)) throw new TimeoutException("Owned Helper did not exit.");
            Log("Stopped Hub-owned MCP Helper.");
        }
        process?.Dispose(); process = null; ownedInstance = null;
        if (ownedConfig is not null && File.Exists(ownedConfig)) File.Delete(ownedConfig);
        ownedConfig = null;
    }

    private static async Task<bool> PortOpen(int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(400));
        using var client = new TcpClient();
        try { await client.ConnectAsync("127.0.0.1", port, timeout.Token); return true; }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { return false; }
    }

    private void Log(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        LogLine?.Invoke(logToken.Length > 0 ? message.Replace(logToken, "[redacted]", StringComparison.Ordinal) : message);
    }
    public void Dispose() { disposed = true; lifetime.Cancel(); StopOwned(); }
}
