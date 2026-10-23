using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ConstantineHub.Adapters.Helper;
using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class HelperLifecycleTests
{
    internal static async Task Run(string package)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "helper-hub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var workspace = Path.Combine(fixture, "workspace"); Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "Example.cs"), "public class Example { public int Value() => 7; }");
        var before = File.ReadAllBytes(Path.Combine(workspace, "Example.cs"));
        Environment.SetEnvironmentVariable("CONSTANTINE_HELPER_SETTINGS", Path.Combine(fixture, "helper.json"));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var executable = Path.Combine(Path.GetFullPath(package), "McpHelper.exe");
        Check(HelperPackage.Verify(executable) == "0.1.1", "standalone package manifest and every file hash verified");
        var settings = new HelperSettings(workspace, executable, port, false, HelperSettings.NewToken());
        settings.Save();
        var logs = new List<string>();
        using (var adapter = new HelperAdapter())
        {
            adapter.LogLine += line => { lock (logs) logs.Add(line); };
            Check((await adapter.GetStatusAsync()).State == AdapterState.Stopped, "configured service initially stopped");
            await adapter.StartAsync();
            var pid = adapter.OwnedPid;
            Check(pid is not null && (await adapter.GetStatusAsync()).State == AdapterState.Running, "Hub-owned service passes actual MCP init/list/call");
            await adapter.StartAsync();
            Check(adapter.OwnedPid == pid, "Start is idempotent for an owned instance");
            using (var observer = new HelperAdapter())
            {
                Check((await observer.GetStatusAsync()).State == AdapterState.External, "another Hub instance observes external without ownership");
                await observer.StopAsync();
                Check(adapter.OwnedPid == pid && (await adapter.GetStatusAsync()).State == AdapterState.Running, "external Stop cannot terminate another owner's Helper");
                try { await observer.RestartAsync(); throw new Exception("external restart accepted"); }
                catch (InvalidOperationException) { Check(true, "external Restart rejected"); }
            }
            Check(adapter.OwnedPid == pid, "observer disposal leaves external process alive");
            var doctor = await adapter.DoctorAsync();
            Check(doctor.Contains("Running") && !doctor.Contains(settings.Token), "Doctor verifies package and protocol without leaking token");
            await adapter.RestartAsync();
            Check(adapter.OwnedPid != pid && (await adapter.GetStatusAsync()).State == AdapterState.Running, "owned Restart produces a ready new instance");
            await adapter.StopAsync();
            Check((await adapter.GetStatusAsync()).State == AdapterState.Stopped, "owned Stop releases endpoint");
            using (var occupied = new TcpListener(IPAddress.Loopback, port))
            {
                occupied.Start();
                try { await adapter.StartAsync(); throw new Exception("occupied endpoint accepted"); }
                catch (InvalidOperationException) { Check(true, "occupied endpoint is rejected without killing its owner"); }
                Check(occupied.Server.IsBound, "foreign listener remains open");
            }
            await adapter.StartAsync();
        }
        using (var afterClose = new HelperAdapter())
            Check((await afterClose.GetStatusAsync()).State == AdapterState.Stopped, "Hub disposal stops its owned service");
        Check(!Directory.GetFiles(fixture, "helper-service-*.json").Any(), "owned runtime config removed on shutdown");
        lock (logs) Check(logs.All(line => !line.Contains(settings.Token)), "lifecycle logs contain no connection token");
        Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(workspace, "Example.cs"))), "Hub lifecycle preserves workspace bytes");
        var extra = Path.Combine(package, "unexpected.txt");
        File.WriteAllText(extra, "test");
        try
        {
            try { HelperPackage.Verify(executable); throw new Exception("unexpected payload accepted"); }
            catch (InvalidDataException) { Check(true, "unexpected package payload rejected"); }
        }
        finally { File.Delete(extra); }
        var disposed = new HelperAdapter(); disposed.Dispose();
        try { await disposed.StartAsync(); throw new Exception("disposed adapter started a process"); }
        catch (ObjectDisposedException) { Check(true, "disposed Hub adapter cannot create a new process"); }
        using (var slow = new TcpListener(IPAddress.Loopback, port))
        {
            slow.Start();
            var server = Task.Run(async () =>
            {
                using var client = await slow.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var received = await stream.ReadAsync(new byte[4096]);
                if (received == 0) return;
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1000\r\n\r\n{"));
                await Task.Delay(4000);
            });
            var watch = Stopwatch.StartNew();
            try { await HelperConnection.ProbeAsync(settings, CancellationToken.None); throw new Exception("incomplete body accepted"); }
            catch (OperationCanceledException) { Check(watch.Elapsed < TimeSpan.FromSeconds(4), "probe bounds response-body reads, not just HTTP headers"); }
            await server;
        }
        Environment.SetEnvironmentVariable("CONSTANTINE_HELPER_SETTINGS", null);
        Console.WriteLine("HELPER HUB LIFECYCLE PASS");
    }
    private static void Check(bool ok, string description)
    {
        if (!ok) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
