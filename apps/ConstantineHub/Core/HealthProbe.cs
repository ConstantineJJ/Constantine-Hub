namespace ConstantineHub.Core;

internal sealed record HealthProbeResult(bool Live, bool Ready, string Message);

internal static class HealthProbe
{
    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(1.5)
    };

    internal static async Task<HealthProbeResult> ProbeAsync(
        int port,
        CancellationToken cancellationToken = default)
    {
        if (port is < 1 or > 65535)
            return new(false, false, "Invalid port");

        var baseUrl = $"http://127.0.0.1:{port}";
        try
        {
            using var live = await Http.GetAsync(baseUrl + "/healthz", cancellationToken);
            if (!live.IsSuccessStatusCode)
                return new(false, false, $"healthz HTTP {(int)live.StatusCode}");

            using var ready = await Http.GetAsync(baseUrl + "/readyz", cancellationToken);
            return ready.IsSuccessStatusCode
                ? new(true, true, $"Ready on 127.0.0.1:{port}")
                : new(true, false, $"Live, not ready (HTTP {(int)ready.StatusCode})");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, false, $"No health response on 127.0.0.1:{port}");
        }
        catch (Exception ex)
        {
            return new(false, false, ex.Message);
        }
    }
}
