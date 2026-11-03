using System.Net.Http;
using System.Text;
using System.Text.Json;
using ConstantineHub.Core;

namespace ConstantineHub.Adapters.Nvidia;

internal sealed record NvidiaAppSnapshot(bool HttpAvailable, string HttpEndpoint, bool StdioBridgeInstalled,
    AdapterStatus TunnelStatus, string HttpMessage, int ToolCount);

// The installed NVIDIA relay and the existing Hub tunnel controller retain process ownership.
internal sealed class NvidiaAppAdapter : TunnelProfileAdapterBase
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(4), MaxResponseContentBufferSize = 512 * 1024 };
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private DateTimeOffset _lastProbe;
    private (bool Ready, string Message, int Tools) _cachedProbe;
    internal NvidiaAppAdapter() : base("constantine-nvidia") { }
    public override string Id => "nvidia-app";
    public override string DisplayName => "NVIDIA App MCP";
    internal static string BridgeExecutable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "NVIDIA Corporation", "NVIDIA App", "McpServer", "NvAppMcpServer.exe");

    internal async Task<NvidiaAppSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = GetLocalEndpoint();
        await _probeGate.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow - _lastProbe > TimeSpan.FromSeconds(15))
            {
                _cachedProbe = await ProbeMcpAsync(endpoint, cancellationToken);
                _lastProbe = DateTimeOffset.UtcNow;
            }
        }
        finally { _probeGate.Release(); }
        return new(_cachedProbe.Ready, endpoint.ToString(), File.Exists(BridgeExecutable),
            await base.GetStatusAsync(cancellationToken), _cachedProbe.Message, _cachedProbe.Tools);
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
        => ExclusiveAsync(StartCoreAsync, cancellationToken);

    public override Task StopAsync(CancellationToken cancellationToken = default)
        => ExclusiveAsync(ct => base.StopAsync(ct), cancellationToken);

    public override Task RestartAsync(CancellationToken cancellationToken = default)
        => ExclusiveAsync(async ct =>
        {
            var current = await TunnelRuntimeProbe.ProbeAsync(ProfileName, ct);
            if (!OwnsRunningTunnel && current.State is TunnelRuntimeState.RunningExpected or TunnelRuntimeState.McpUnavailable)
                throw new ExternalTunnelControlException(DisplayName);
            if (current.State == TunnelRuntimeState.Occupied) throw new InvalidOperationException(current.Message);
            await base.StopAsync(ct);
            await StartCoreAsync(ct);
        }, cancellationToken);

    private async Task ExclusiveAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        await _operationGate.WaitAsync(token);
        try { await action(token); }
        finally { _operationGate.Release(); }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(BridgeExecutable))
            throw new FileNotFoundException("NVIDIA App MCP relay is missing. Enable AI Agents and App Connections in NVIDIA App.", BridgeExecutable);
        var profile = TunnelProfile.Load("constantine-nvidia");
        if (!IsInstalledRelayCommand(profile.McpCommand))
            throw new InvalidDataException("The NVIDIA tunnel must use NVIDIA App's installed NvAppMcpServer.exe relay.");
        await base.StartAsync(cancellationToken);
        _lastProbe = default;
    }

    internal static bool IsInstalledRelayCommand(string command)
    {
        try { return string.Equals(TunnelProfile.CommandPath(command), Path.GetFullPath(BridgeExecutable), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or NotSupportedException) { return false; }
    }

    public override async Task<string> DoctorAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try { return await DoctorCoreAsync(cancellationToken); }
        finally { _operationGate.Release(); }
    }

    private async Task<string> DoctorCoreAsync(CancellationToken cancellationToken)
    {
        _lastProbe = default;
        var status = await GetSnapshotAsync(cancellationToken);
        var lines = new List<string> { "NVIDIA App MCP", "Local HTTP: " + status.HttpEndpoint,
            status.HttpMessage, "Tools discovered: " + status.ToolCount,
            "Installed stdio relay: " + (status.StdioBridgeInstalled ? "yes" : "missing"),
            "Tunnel: " + status.TunnelStatus.Summary,
            "The tunnel uses NVIDIA's stdio relay; HTTP and tunnel readiness are reported separately." };
        try { lines.Add(await base.DoctorAsync(cancellationToken)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { lines.Add("Tunnel doctor: " + ex.Message); }
        return string.Join(Environment.NewLine, lines);
    }

    internal static Uri GetLocalEndpoint(string? discoveryJson = null)
    {
        try
        {
            discoveryJson ??= File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NVIDIA Corporation", "NvAppMcpServer", "server.json"));
            using var document = JsonDocument.Parse(discoveryJson);
            if (document.RootElement.TryGetProperty("http", out var property) && property.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(property.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp &&
                uri.Host == "127.0.0.1" && uri.Port > 0 && string.IsNullOrEmpty(uri.UserInfo) &&
                uri.AbsolutePath is "/mcp" or "/mcp/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
                return uri;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new("http://127.0.0.1:13508/mcp");
    }

    private static async Task<(bool, string, int)> ProbeMcpAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        string? session = null;
        var protocolVersion = "2025-03-26";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        var token = deadline.Token;
        try
        {
            using var initialize = await SendAsync(endpoint, new { jsonrpc = "2.0", id = 1, method = "initialize",
                @params = new { protocolVersion = "2025-03-26", capabilities = new { },
                    clientInfo = new { name = "ConHub.", version = typeof(NvidiaAppAdapter).Assembly.GetName().Version?.ToString(3) } } }, null, token);
            if (initialize.Headers.TryGetValues("Mcp-Session-Id", out var values)) session = values.FirstOrDefault();
            using var init = await ReadReplyAsync(initialize, token, expectedId: 1);
            if (!init.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("serverInfo", out var server) ||
                server.GetProperty("name").GetString() != "nvidia-app")
                return (false, "The local endpoint did not identify itself as nvidia-app.", 0);
            protocolVersion = result.GetProperty("protocolVersion").GetString() ?? protocolVersion;
            using var initialized = await SendAsync(endpoint, new { jsonrpc = "2.0", method = "notifications/initialized" }, session, token, protocolVersion);
            initialized.EnsureSuccessStatusCode();
            using var response = await SendAsync(endpoint, new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } }, session, token, protocolVersion);
            using var tools = await ReadReplyAsync(response, token);
            var count = tools.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength();
            return (count > 0, $"NVIDIA App MCP responded; {count} tools available.", count);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return (false, "NVIDIA App MCP probe timed out.", 0); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException)
            { return (false, "NVIDIA App MCP unavailable: " + ex.Message, 0); }
        finally
        {
            if (session is not null)
            {
                try
                {
                    using var close = new HttpRequestMessage(HttpMethod.Delete, endpoint);
                    close.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
                    close.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    using var response = await Http.SendAsync(close, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
            }
        }
    }

    private static Task<HttpResponseMessage> SendAsync(Uri endpoint, object body, string? session, CancellationToken ct,
        string protocolVersion = "2025-03-26")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
        if (session is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
        return SendAndDisposeAsync(request, ct);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using (request) return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    internal static async Task<JsonDocument> ReadReplyAsync(HttpResponseMessage response, CancellationToken ct, int expectedId = 2)
    {
        response.EnsureSuccessStatusCode();
        const int limit = 512 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new IOException("NVIDIA MCP diagnostic response exceeds 512 KiB.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        var eventStream = response.Content.Headers.ContentType?.MediaType == "text/event-stream";
        var processedCharacters = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)bytes.Length)), ct);
            if (read == 0) break;
            bytes.Write(buffer, 0, read);
            if (bytes.Length > limit) throw new IOException("NVIDIA MCP diagnostic response exceeds 512 KiB.");
            // A Streamable HTTP event stream may stay open after the reply event.
            if (eventStream)
            {
                var text = Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length).Replace("\r\n", "\n");
                while (text.IndexOf("\n\n", processedCharacters, StringComparison.Ordinal) is var end && end >= 0)
                {
                    var frame = text[processedCharacters..end];
                    processedCharacters = end + 2;
                    var data = string.Join("\n", frame.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
                    if (string.IsNullOrWhiteSpace(data)) continue;
                    var candidate = JsonDocument.Parse(data);
                    if (MatchesReply(candidate.RootElement, expectedId)) return candidate;
                    candidate.Dispose();
                }
            }
        }
        var json = Encoding.UTF8.GetString(bytes.ToArray());
        if (eventStream) throw new JsonException("NVIDIA MCP event stream ended without the requested response ID.");
        var document = JsonDocument.Parse(json);
        if (MatchesReply(document.RootElement, expectedId)) return document;
        document.Dispose();
        throw new JsonException("NVIDIA MCP response ID did not match the request.");
    }

    private static bool MatchesReply(JsonElement root, int expectedId)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var id) &&
           id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var value) && value == expectedId &&
           (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _));
}
