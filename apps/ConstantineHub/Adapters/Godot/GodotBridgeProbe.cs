using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ConstantineHub.Adapters.Godot;

internal sealed record GodotBridgeStatus(bool Connected, string Message, string? ProjectRoot = null);

internal static class GodotBridgeProbe
{
    internal static async Task<GodotBridgeStatus> ProbeAsync(string host, int port, string selectedProject,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(800);
        var connected = false;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            connected = true;
            await using var stream = client.GetStream();
            var id = Guid.NewGuid().ToString("N");
            var request = JsonSerializer.Serialize(new { id, op = "status", @params = new { } });
            await stream.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), timeout.Token);
            using var response = new MemoryStream();
            var buffer = new byte[2048];
            while (response.Length < 65_536)
            {
                var count = await stream.ReadAsync(buffer, timeout.Token);
                if (count == 0) break;
                var newline = Array.IndexOf(buffer, (byte)'\n', 0, count);
                response.Write(buffer, 0, newline < 0 ? count : newline);
                if (newline >= 0) break;
            }
            if (response.Length >= 65_536) return new(false, "Bridge status response exceeded the size limit.");
            using var document = JsonDocument.Parse(response.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var replyId) || replyId.GetString() != id ||
                !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("result", out var result) || !result.TryGetProperty("project_path", out var project))
                return new(false, "Bridge did not return a valid project identity.");
            var reported = project.GetString();
            if (string.IsNullOrWhiteSpace(reported) || !GodotProjectRegistry.SameProject(reported, selectedProject))
                return new(false, $"Bridge belongs to another project: {reported ?? "unknown"}. Open the selected project.", reported);
            return new(true, $"Connected {host}:{port}", reported);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, $"Bridge {host}:{port} timed out {(connected ? "waiting for status" : "connecting")} (800 ms)."); }
        catch (SocketException ex)
        { return new(false, $"Bridge {host}:{port} connection failed ({ex.SocketErrorCode})."); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or ArgumentException)
        { return new(false, $"Bridge {host}:{port} returned an invalid or incomplete status ({ex.GetType().Name})."); }
    }
}
