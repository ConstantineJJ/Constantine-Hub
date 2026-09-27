using System.Net.Sockets;

namespace ConstantineHub.Core;

internal static class PortProbe
{
    internal static async Task<bool> CanConnectAsync(
        string host,
        int port,
        int timeoutMs = 350,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            return false;

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMs);
            await client.ConnectAsync(host, port, timeout.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
