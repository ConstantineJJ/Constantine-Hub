using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ConstantineHub.Adapters.Godot;

namespace ConstantineHub.SmokeTests;

internal static class GodotProjectTests
{
    internal static void Run(string fixture)
    {
        var first = Path.Combine(fixture, "first-project");
        var second = Path.Combine(fixture, "second-project");
        foreach (var path in new[] { first, second })
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "project.godot"), "[application]\nconfig/name=\"Registry fixture\"\n");
        }
        var before = GodotSettingsStore.Load();
        var registered = GodotProjectRegistry.Register(Path.Combine(second, "project.godot"));
        GodotProjectRegistry.Register(second + Path.DirectorySeparatorChar);
        var entries = GodotProjectRegistry.Load();
        Check(entries.Any(e => GodotProjectRegistry.SameProject(e.ProjectRoot, before.ProjectRoot)), "current Godot project is retained in registry");
        Check(entries.Count(e => GodotProjectRegistry.SameProject(e.ProjectRoot, second)) == 1 && registered.Name == "Registry fixture", "register project.godot persists one normalized named project");
        Check(GodotSettingsStore.Load() == before, "registering another project preserves the active configuration");
        try { GodotProjectRegistry.Register(fixture + "-missing"); throw new Exception("Missing project accepted"); }
        catch (DirectoryNotFoundException) { }
        Directory.Delete(second, recursive: true);
        Check(GodotProjectRegistry.Load().Single(e => GodotProjectRegistry.SameProject(e.ProjectRoot, second)).Exists == false, "missing registered project stays visible without preventing startup");

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var reply = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var request = JsonDocument.Parse((await reader.ReadLineAsync())!);
            var response = JsonSerializer.Serialize(new { id = request.RootElement.GetProperty("id").GetString(), ok = true, result = new { project_path = first } });
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response + "\n"));
        });
        var wrong = GodotBridgeProbe.ProbeAsync("127.0.0.1", port, fixture).GetAwaiter().GetResult();
        reply.GetAwaiter().GetResult();
        Check(!wrong.Connected && wrong.Message.Contains("another project", StringComparison.OrdinalIgnoreCase), "open port belonging to another Godot project is not shown connected");
        TestSelection(before, first, fixture);
        TestLegacyPluginMigration(before, fixture);
    }

    private static void TestLegacyPluginMigration(GodotSettings settings, string fixture)
    {
        const string legacyPath = "res://addons/godot_mcp/plugin.cfg";
        const string hubPath = "res://addons/constantine_mcp/plugin.cfg";
        const string otherPath = "res://addons/unrelated/plugin.cfg";
        const string legacyAutoload = "GodotMCPRuntime=\"*res://addons/godot_mcp/runtime_bridge.gd\"";
        var project = Path.Combine(fixture, "legacy-migration");
        var legacy = Path.Combine(project, "addons", "godot_mcp");
        Directory.CreateDirectory(legacy);
        var config = Path.Combine(project, "project.godot");
        var original = "[application]\nconfig/name=\"Legacy migration\"\n[autoload]\n" + legacyAutoload +
            "\nOtherAutoload=\"*res://other.gd\"\n[editor_plugins]\nenabled=PackedStringArray(\"" +
            legacyPath + "\", \"" + otherPath + "\", \"" + hubPath + "\")\n";
        File.WriteAllText(config, original);
        File.WriteAllText(Path.Combine(legacy, "plugin.cfg"), "[plugin]\nname=\"Godot MCP for ChatGPT\"\nscript=\"plugin.gd\"\n");
        var oldScript = "@tool\nextends EditorPlugin\nconst EDITOR_PORT := 6262\n";
        File.WriteAllText(Path.Combine(legacy, "plugin.gd"), oldScript);
        GodotPluginInstaller.InstallOrUpdate(settings with { ProjectRoot = project });
        var migrated = File.ReadAllText(config);
        Check(!migrated.Contains(legacyPath) && !migrated.Contains(legacyAutoload) && migrated.Contains(hubPath) &&
            migrated.Contains(otherPath) && migrated.Contains("OtherAutoload=\"*res://other.gd\""),
            "known conflicting legacy editor and runtime bridge are disabled even when Hub plugin is already enabled");
        Check(File.ReadAllText(config + ".constantine-before-plugin.bak") == original && File.ReadAllText(Path.Combine(legacy, "plugin.gd")) == oldScript,
            "legacy migration backs up project configuration and preserves old addon files");
        GodotPluginInstaller.InstallOrUpdate(settings with { ProjectRoot = project });
        Check(File.ReadAllText(config) == migrated, "legacy bridge migration is idempotent");
        File.WriteAllText(config, original);
        File.WriteAllText(Path.Combine(legacy, "plugin.cfg"), "[plugin]\nname=\"Unrelated plugin\"\nscript=\"plugin.gd\"\n");
        GodotPluginInstaller.InstallOrUpdate(settings with { ProjectRoot = project });
        Check(File.ReadAllText(config) == original, "unrecognized addon at legacy path and its autoload are left unchanged");
    }

    private static void TestSelection(GodotSettings before, string first, string fixture)
    {
        var previousDirectory = Environment.GetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_DIR");
        var previousFile = Environment.GetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_FILE");
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        var settings = before with { TunnelProfile = "project-select-fixture", HealthPort = port, TunnelId = "tunnel_project_select" };
        var command = Path.Combine(fixture, "fake-mcp.cmd");
        var address = $"127.0.0.1:{port}";
        var profile = Path.Combine(fixture, settings.TunnelProfile + ".yaml");
        File.WriteAllText(profile, $"tunnel_id: {settings.TunnelId}\ncommand: {command}\nlisten_addr: {address}\n");
        Environment.SetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_DIR", fixture);
        Environment.SetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_FILE", null);
        using var cancellation = new CancellationTokenSource();
        try
        {
            // With a missing profile no service is running; selection only changes settings.
            GodotSettingsStore.Save(before with { TunnelProfile = "not-running-project-fixture" });
            using (var stopped = new GodotAdapter()) stopped.SelectProjectAsync(first).GetAwaiter().GetResult();
            var selected = GodotSettingsStore.Load();
            Check(GodotProjectRegistry.SameProject(selected.ProjectRoot, first) &&
                selected.EditorPort == before.EditorPort && selected.RuntimePort == before.RuntimePort && selected.ToolsCRoot == before.ToolsCRoot,
                "switching a stopped adapter saves selected project and preserves bridge and knowledge settings");

            GodotSettingsStore.Save(settings);
            var serve = Task.Run(async () =>
            {
                try
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        using var client = await server.AcceptTcpClientAsync(cancellation.Token);
                        await using var stream = client.GetStream();
                        using var reader = new StreamReader(stream, leaveOpen: true);
                        var request = await reader.ReadLineAsync(cancellation.Token);
                        if (request is null) continue;
                        var body = request.Contains("/api/status") ? JsonSerializer.Serialize(new
                        {
                            control_plane_tunnel_id = settings.TunnelId, health_listen_addr = address,
                            channels = new[] { new { name = "main", details = new[] { new { key = "command", value = command } } } }
                        }) : "{}";
                        await stream.WriteAsync(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " +
                            Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body), cancellation.Token);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            });
            using (var external = new GodotAdapter())
            {
                try { external.SelectProjectAsync(first).GetAwaiter().GetResult(); throw new Exception("External project switch was accepted"); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("outside Hub")) { }
                Check(GodotSettingsStore.Load() == settings, "external running tunnel blocks selection before active configuration changes");
            }
            cancellation.Cancel();
            serve.GetAwaiter().GetResult();
        }
        finally
        {
            cancellation.Cancel();
            GodotSettingsStore.Save(before);
            Environment.SetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_DIR", previousDirectory);
            Environment.SetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_FILE", previousFile);
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
