using ConstantineHub.Adapters.Nvidia;
using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

// Explicit workstation acceptance only; never called by the hermetic CI suite.
internal static class NvidiaManagedTests
{
    internal static async Task RunAsync(string expectedTunnelId)
    {
        var profile = TunnelProfile.Load("constantine-nvidia");
        Check(profile.TunnelId == expectedTunnelId, "explicit NVIDIA tunnel identity matches the configured profile");
        using var adapter = new NvidiaAppAdapter();
        Check((await adapter.GetStatusAsync()).State == AdapterState.Stopped,
            "NVIDIA fixture starts with no existing tunnel to adopt or replace");
        Check(JensenAnimationControl.ConnectionState(await adapter.GetSnapshotAsync()) == JensenConnectionState.Disconnected,
            "Animation stays disconnected when only the actual local MCP is available");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await adapter.StartAsync(deadline.Token);
        var started = await adapter.GetSnapshotAsync(deadline.Token);
        Check(started.HttpAvailable && started.ToolCount > 0 && started.TunnelStatus.State == AdapterState.Running,
            "ConHub-owned NVIDIA tunnel and actual local MCP both reach ready state");
        Check(JensenAnimationControl.ConnectionState(started) == JensenConnectionState.Connected,
            "Animation follows the real ready local MCP and tunnel snapshot");
        var tunnelClient = ExecutableLocator.FindTunnelClient() ?? throw new Exception("Missing tunnel-client");
        var health = await ProcessCapture.RunAsync(tunnelClient,
            ["health", "--port", profile.HealthPort.ToString(), "--json"], deadline.Token);
        Check(health.ExitCode == 0, "native tunnel-client confirms the owned daemon's health and readiness");
        using (var report = System.Text.Json.JsonDocument.Parse(health.Output))
            Console.WriteLine("Native health report fields: " + string.Join(", ", report.RootElement.EnumerateObject().Select(p => p.Name)));
        var pid = started.TunnelStatus.Details!["Owned PID"];
        await adapter.StartAsync(deadline.Token);
        Check((await adapter.GetStatusAsync(deadline.Token)).Details!["Owned PID"] == pid,
            "repeated NVIDIA Start preserves the same owned process");
        using (var observer = new NvidiaAppAdapter())
        {
            Check((await observer.GetStatusAsync(deadline.Token)).State == AdapterState.External,
                "another ConHub instance reports the NVIDIA tunnel as externally owned");
            await observer.StopAsync(deadline.Token);
            try { await observer.RestartAsync(deadline.Token); throw new Exception("External restart accepted"); }
            catch (ExternalTunnelControlException) { }
            Check((await adapter.GetStatusAsync(deadline.Token)).Details!["Owned PID"] == pid,
                "external Stop/Restart leave the NVIDIA tunnel owner intact");
        }
        await adapter.RestartAsync(deadline.Token);
        var restarted = await adapter.GetStatusAsync(deadline.Token);
        Check(restarted.State == AdapterState.Running && restarted.Details!["Owned PID"] != pid,
            "owned NVIDIA Restart replaces its tunnel and restores readiness");
        await adapter.StopAsync(deadline.Token);
        Check((await adapter.GetStatusAsync(deadline.Token)).State == AdapterState.Stopped,
            "owned NVIDIA Stop releases its loopback listener");
        Check((await adapter.GetSnapshotAsync(deadline.Token)).HttpAvailable,
            "NVIDIA App's external local service remains available after tunnel Stop");
        Check(JensenAnimationControl.ConnectionState(await adapter.GetSnapshotAsync(deadline.Token)) == JensenConnectionState.Disconnected,
            "Animation follows the real tunnel disconnect while the local MCP stays available");
    }

    private static void Check(bool ok, string description)
    {
        if (!ok) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
