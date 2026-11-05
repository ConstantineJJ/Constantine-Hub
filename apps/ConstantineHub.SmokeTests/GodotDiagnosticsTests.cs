using System.Net;
using System.Net.Sockets;
using System.Reflection;
using ConstantineHub.Adapters.Godot;
using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class GodotDiagnosticsTests
{
    internal static async Task RunAsync()
    {
        using var godot = new GodotAdapter();
        var arguments = (IReadOnlyList<string>)typeof(TunnelProfileAdapterBase)
            .GetProperty("TunnelRunArguments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(godot)!;
        Check(arguments.SequenceEqual(new[] { "run", "--profile", godot.Settings.TunnelProfile, "--mcp.max-concurrent-requests", "1" }),
            "Godot always launches one active MCP worker to prevent shared-stdio response misrouting");
        var snapshot = new GodotAdapterSnapshot(true, Path.GetTempPath(), new(true, true, "", "", "fixture"),
            true, null, true, false, "fixture", true,
            new("godot", "Godot MCP", AdapterState.Running, "fixture"), RuntimeBridgeIssue: "runtime timed out");
        Check(snapshot.RecoveryChecksPassed, "runtime unavailability alone does not restart a healthy tunnel/editor connection");
        Check(!(snapshot with { TunnelStatus = snapshot.TunnelStatus with { State = AdapterState.Degraded } }).RecoveryChecksPassed &&
            !(snapshot with { EditorBridgeConnected = false }).RecoveryChecksPassed,
            "degraded MCP or missing editor identity cannot pass recovery checks");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var pending = GodotBridgeProbe.ProbeAsync("127.0.0.1", port, Path.GetTempPath());
        using var client = await listener.AcceptTcpClientAsync();
        var timeout = await pending;
        Check(!timeout.Connected && timeout.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase),
            "runtime listener with no response is diagnosed as timeout");
        listener.Stop();
        var refused = await GodotBridgeProbe.ProbeAsync("127.0.0.1", port, Path.GetTempPath());
        Check(!refused.Connected && (refused.Message.Contains("ConnectionRefused") || refused.Message.Contains("timed out connecting")),
            "missing runtime listener is distinct from a nonresponding listener");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        try
        {
            await GodotBridgeProbe.ProbeAsync("127.0.0.1", port, Path.GetTempPath(), cancel.Token);
            throw new Exception("Caller cancellation was swallowed.");
        }
        catch (OperationCanceledException) { }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
