using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class TunnelLifecycleTests
{
    internal static int Serve(string name)
    {
        var profile = TunnelProfile.Load(name);
        var listener = new TcpListener(IPAddress.Loopback, profile.HealthPort);
        listener.Start();
        while (true)
        {
            using var client = listener.AcceptTcpClient();
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                var request = reader.ReadLine();
                if (request is null) continue; // A TCP-only readiness probe.
                var body = request.Contains("/api/status") ? JsonSerializer.Serialize(new
                {
                    control_plane_tunnel_id = profile.TunnelId,
                    health_listen_addr = profile.ListenAddress,
                    channels = new[] { new { name = "main", details = new[] { new { key = "command", value = profile.McpCommand } } } }
                }) : "{}";
                var response = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " +
                    Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body);
                stream.Write(response);
            }
            catch (IOException) { }
        }
    }

    internal static void Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "hub-tunnel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var names = new[] { "TUNNEL_CLIENT_PROFILE_DIR", "TUNNEL_CLIENT_PROFILE_FILE", "CONSTANTINE_TUNNEL_CLIENT", "CONTROL_PLANE_API_KEY" };
        var old = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        Process? external = null;
        try
        {
            Environment.SetEnvironmentVariable(names[0], fixture);
            Environment.SetEnvironmentVariable(names[1], null);
            Environment.SetEnvironmentVariable(names[2], Environment.ProcessPath);
            Environment.SetEnvironmentVariable(names[3], "isolated-smoke-fixture");
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var profilePath = Path.Combine(fixture, "smoke-tunnel.yaml");
            var yaml = $"tunnel_id: smoke_tunnel\ncommand: fake-mcp.cmd\nlisten_addr: 127.0.0.1:{port}\n";
            File.WriteAllText(profilePath, yaml);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("run");
            start.ArgumentList.Add("--profile");
            start.ArgumentList.Add("smoke-tunnel");
            external = Process.Start(start)!;
            using var adapter = new TestAdapter();
            WaitReady();
            Require(adapter.GetStatusAsync().GetAwaiter().GetResult().State == AdapterState.External, "pre-existing tunnel remains external");
            adapter.StartAsync().GetAwaiter().GetResult();
            Require(adapter.Pid is null, "Start adopts health without taking external process ownership");
            try { adapter.RestartAsync().GetAwaiter().GetResult(); throw new Exception("External restart unexpectedly succeeded"); }
            catch (ExternalTunnelControlException ex) { Require(ex.Message.Contains("app or terminal"), "external restart returns expected launcher guidance"); }
            adapter.StopAsync().GetAwaiter().GetResult();
            Require(!external.HasExited && adapter.Pid is null, "Stop and Restart do not kill or replace the external process");
            File.WriteAllText(profilePath, yaml.Replace("smoke_tunnel", "different_tunnel"));
            try { adapter.RestartAsync().GetAwaiter().GetResult(); throw new Exception("Occupied port restart unexpectedly succeeded"); }
            catch (InvalidOperationException) { Require(!external.HasExited, "mismatched tunnel is a real error and remains untouched"); }
            File.WriteAllText(profilePath, yaml);
            external.Kill();
            external.WaitForExit(3000);
            adapter.StartAsync().GetAwaiter().GetResult();
            var firstPid = adapter.Pid;
            Require(firstPid is not null && adapter.GetStatusAsync().GetAwaiter().GetResult().State == AdapterState.Running, "Hub-owned Start reaches readiness");
            adapter.RestartAsync().GetAwaiter().GetResult();
            Require(adapter.Pid is not null && adapter.Pid != firstPid && adapter.GetStatusAsync().GetAwaiter().GetResult().State == AdapterState.Running,
                "Hub-owned Restart replaces its process and reaches readiness");
            adapter.StopAsync().GetAwaiter().GetResult();
            Require(adapter.Pid is null && adapter.GetStatusAsync().GetAwaiter().GetResult().State == AdapterState.Stopped, "Hub-owned Stop releases its process and listener");
        }
        finally
        {
            if (external is not null) { if (!external.HasExited) { external.Kill(); external.WaitForExit(3000); } external.Dispose(); }
            foreach (var name in names) Environment.SetEnvironmentVariable(name, old[name]);
            Directory.Delete(fixture, recursive: true);
        }
    }

    private static void WaitReady()
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (TunnelRuntimeProbe.ProbeAsync("smoke-tunnel").GetAwaiter().GetResult().State == TunnelRuntimeState.RunningExpected) return;
            Thread.Sleep(50);
        }
        throw new TimeoutException("Fixture tunnel failed to become ready.");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    private sealed class TestAdapter() : TunnelProfileAdapterBase("smoke-tunnel")
    {
        public override string Id => "smoke";
        public override string DisplayName => "Smoke tunnel";
        internal int? Pid => OwnedTunnelPid;
    }
}
