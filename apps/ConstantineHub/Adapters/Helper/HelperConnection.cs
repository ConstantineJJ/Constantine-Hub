using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ConstantineHub.Adapters.Helper;

internal sealed record HelperIdentity(string Version, string InstanceId, string WorkspaceRoot);

internal static class HelperConnection
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static int nextId;
    internal static async Task<HelperIdentity> ProbeAsync(HelperSettings settings, CancellationToken cancellationToken)
    {
        using var init = await Request(settings, "initialize", new { protocolVersion = "2025-11-25", capabilities = new { },
            clientInfo = new { name = "ConstantineHub", version = "0.3.0" } }, cancellationToken);
        if (!init.RootElement.TryGetProperty("result", out var initialized) || !initialized.TryGetProperty("serverInfo", out _))
            throw new InvalidDataException("Helper MCP initialization failed.");
        using var listed = await Request(settings, "tools/list", new { }, cancellationToken);
        var names = listed.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal);
        if (names.Count != 7 || !names.Contains("helper_status") || !names.Contains("helper_refactor_preview") || names.Contains("helper_refactor_apply"))
            throw new InvalidDataException("Unsupported Helper tool catalog.");
        using var called = await Request(settings, "tools/call", new { name = "helper_status", arguments = new { } }, cancellationToken);
        var result = called.RootElement.GetProperty("result");
        if (result.TryGetProperty("isError", out var error) && error.GetBoolean()) throw new InvalidDataException("Helper status call failed.");
        var body = result.GetProperty("content").EnumerateArray().First(c => c.GetProperty("type").GetString() == "text").GetProperty("text").GetString()!;
        using var status = JsonDocument.Parse(body);
        var root = status.RootElement;
        var version = root.GetProperty("version").GetString()!;
        var workspace = root.GetProperty("workspace_root").GetString()!;
        if (version != "0.1.0" || root.GetProperty("apply_available").GetBoolean() ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.WorkspaceRoot)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Helper version, workspace or capabilities disagree with configuration.");
        return new(version, root.GetProperty("instance_id").GetString()!, workspace);
    }

    private static async Task<JsonDocument> Request(HelperSettings settings, string method, object parameters, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        token = timeout.Token;
        var id = Interlocked.Increment(ref nextId);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{settings.Port}/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        request.Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, token);
            if (count == 0) break;
            if (body.Length + count > 131072) throw new InvalidDataException("Helper probe response exceeds limit.");
            body.Write(buffer, 0, count);
        }
        var text = Encoding.UTF8.GetString(body.ToArray());
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            text = text.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal))
                .Select(l => l[5..].Trim()).First(l => l.StartsWith('{'));
        var document = JsonDocument.Parse(text);
        if (!document.RootElement.TryGetProperty("id", out var replyId) || replyId.GetInt32() != id || document.RootElement.TryGetProperty("error", out _))
        { document.Dispose(); throw new InvalidDataException("Helper probe returned an invalid MCP response."); }
        return document;
    }
}
