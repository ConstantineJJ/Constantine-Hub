using System.Text.Json;
using ConstantineHub.Core;

namespace ConstantineHub.Adapters.Nvidia;

internal sealed record NvidiaAppSnapshot(
    bool HttpAvailable,
    string HttpEndpoint,
    bool StdioBridgeInstalled,
    AdapterStatus TunnelStatus);

internal sealed class NvidiaAppAdapter : TunnelProfileAdapterBase
{
    internal NvidiaAppAdapter() : base("constantine-nvidia") { }

    public override string Id => "nvidia-app";
    public override string DisplayName => "NVIDIA App MCP";

    private static string BridgeExecutable => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "NVIDIA Corporation", "NVIDIA App", "McpServer", "NvAppMcpServer.exe");

    internal async Task<NvidiaAppSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var endpoint = GetLocalEndpoint();
        var connected = await PortProbe.CanConnectAsync(
            "127.0.0.1", endpoint.Port, timeoutMs: 450,
            cancellationToken: cancellationToken);
        return new(
            connected,
            endpoint.ToString(),
            File.Exists(BridgeExecutable),
            await base.GetStatusAsync(cancellationToken));
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(BridgeExecutable))
            throw new FileNotFoundException(
                "NVIDIA App MCP stdio relay is not installed. Update NVIDIA App and enable AI Agents and App Connections.",
                BridgeExecutable);
        await base.StartAsync(cancellationToken);
    }

    public override async Task<string> DoctorAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetSnapshotAsync(cancellationToken);
        var lines = new List<string>
        {
            "NVIDIA App MCP",
            "Stdio relay: " + (status.StdioBridgeInstalled ? "installed" : "missing"),
            "Local HTTP: " + (status.HttpAvailable ? "listening" : "not listening") +
                " (" + status.HttpEndpoint + ")",
            "Tunnel: " + status.TunnelStatus.Summary,
            "Note: the stdio relay uses NVIDIA App's per-user pipe; HTTP availability is informational."
        };
        try { lines.Add(await base.DoctorAsync(cancellationToken)); }
        catch (Exception ex) { lines.Add("Tunnel doctor: " + ex.Message); }
        return string.Join(Environment.NewLine, lines);
    }

    private static Uri GetLocalEndpoint()
    {
        var discovery = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NVIDIA Corporation", "NvAppMcpServer", "server.json");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(discovery));
            if (document.RootElement.TryGetProperty("http", out var property) &&
                property.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(property.GetString(), UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttp &&
                uri.Host == "127.0.0.1" &&
                (uri.AbsolutePath == "/mcp" || uri.AbsolutePath == "/mcp/") &&
                string.IsNullOrEmpty(uri.Query) &&
                string.IsNullOrEmpty(uri.Fragment))
                return uri;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return new Uri("http://127.0.0.1:13508/mcp");
    }
}
