using System.Text.Json;

namespace ConstantineHub.Core;

internal enum TunnelRuntimeState
{
    Stopped,
    RunningExpected,
    Occupied,
    InvalidProfile
}

internal sealed record TunnelRuntimeStatus(
    TunnelRuntimeState State,
    string Message,
    TunnelProfile? Profile = null);

internal static class TunnelRuntimeProbe
{
    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(1.5)
    };

    internal static async Task<TunnelRuntimeStatus> ProbeAsync(
        string profileName,
        CancellationToken cancellationToken = default)
    {
        TunnelProfile profile;
        try
        {
            profile = TunnelProfile.Load(profileName);
        }
        catch (Exception ex)
        {
            return new(TunnelRuntimeState.InvalidProfile, ex.Message);
        }

        if (!await PortProbe.CanConnectAsync("127.0.0.1", profile.HealthPort, 500, cancellationToken))
            return new(
                TunnelRuntimeState.Stopped,
                $"Health port {profile.HealthPort} is not accepting TCP connections.",
                profile);

        try
        {
            var baseUrl = $"http://127.0.0.1:{profile.HealthPort}";
            using var ready = await Http.GetAsync(baseUrl + "/readyz", cancellationToken);
            using var status = await Http.GetAsync(baseUrl + "/api/status", cancellationToken);
            if (!ready.IsSuccessStatusCode || !status.IsSuccessStatusCode)
                return new(
                    TunnelRuntimeState.Occupied,
                    $"Port {profile.HealthPort} accepts TCP but does not expose a ready tunnel-client status endpoint.",
                    profile);

            using var document = JsonDocument.Parse(await status.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var liveTunnelId = root.TryGetProperty("control_plane_tunnel_id", out var id)
                ? id.GetString()
                : null;
            var liveAddress = root.TryGetProperty("health_listen_addr", out var address)
                ? address.GetString()
                : null;

            string? liveCommand = null;
            if (root.TryGetProperty("channels", out var channels) && channels.ValueKind == JsonValueKind.Array)
            {
                foreach (var channel in channels.EnumerateArray())
                {
                    if (!channel.TryGetProperty("name", out var channelName) || channelName.GetString() != "main")
                        continue;
                    if (!channel.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("key", out var key) || key.GetString() != "command")
                            continue;
                        if (detail.TryGetProperty("value", out var value))
                            liveCommand = value.GetString();
                    }
                }
            }

            if (string.Equals(liveTunnelId, profile.TunnelId, StringComparison.Ordinal) &&
                string.Equals(liveAddress, profile.ListenAddress, StringComparison.Ordinal) &&
                SameCommand(liveCommand, profile.McpCommand))
            {
                return new(
                    TunnelRuntimeState.RunningExpected,
                    $"Expected tunnel is ready on {profile.ListenAddress}.",
                    profile);
            }

            return new(
                TunnelRuntimeState.Occupied,
                $"Port {profile.HealthPort} belongs to a different tunnel or MCP target.",
                profile);
        }
        catch (Exception ex)
        {
            return new(
                TunnelRuntimeState.Occupied,
                $"Port {profile.HealthPort} is occupied but its identity could not be verified: {ex.Message}",
                profile);
        }
    }

    private static bool SameCommand(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            var leftPath = Path.GetFullPath(left.Replace('/', Path.DirectorySeparatorChar));
            var rightPath = Path.GetFullPath(right.Replace('/', Path.DirectorySeparatorChar));
            return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
