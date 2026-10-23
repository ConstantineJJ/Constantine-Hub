using System.Diagnostics;
using System.Security.Cryptography;
using ConstantineHub.Adapters.Helper;
using ConstantineHub.Adapters.Godot;
using ConstantineHub.Updater;
using HubUpdaterProgram = ConstantineHub.Updater.Program;

namespace ConstantineHub.SmokeTests;

internal static class ReleaseUpgradeTests
{
    internal static async Task Run(string previous, string candidate)
    {
        var previousVersion = FileVersionInfo.GetVersionInfo(Path.Combine(previous, "ConstantineHub.dll")).FileVersion;
        var fixture = Path.Combine(Path.GetTempPath(), "hub-upgrade-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(fixture, "installation");
        var backup = Path.Combine(fixture, "recovery");
        Directory.CreateDirectory(target);
        foreach (var source in Directory.GetFiles(previous, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(previous, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination);
        }
        File.WriteAllText(Path.Combine(target, "user-settings.local.json"), "preserve me");
        var original = Hashes(target);
        var replaced = new List<string>(); var created = new List<string>();
        using (var held = new FileStream(Path.Combine(target, "ConstantineHub.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { HubUpdaterProgram.ApplyFiles(candidate, target, backup, replaced, created, TimeSpan.FromMilliseconds(150)); throw new Exception("locked upgrade accepted"); }
            catch (IOException) { Check(Hashes(target).OrderBy(p => p.Key).SequenceEqual(original.OrderBy(p => p.Key)), $"locked {previousVersion} installation remains byte-for-byte unchanged"); }
        }
        HubUpdaterProgram.ApplyFiles(candidate, target, backup, replaced, created, TimeSpan.FromSeconds(3));
        Check(FileVersionInfo.GetVersionInfo(Path.Combine(target, "ConstantineHub.dll")).FileVersion == "0.3.1.0", $"actual {previousVersion} payload upgraded to v0.3.1");
        Check(File.ReadAllText(Path.Combine(target, "user-settings.local.json")) == "preserve me", "upgrade preserves user installation files");
        var exe = Path.Combine(target, "adapters", "mcp-helper", "McpHelper.exe");
        Check(HelperPackage.Verify(exe) == "0.1.1", "upgraded installation contains complete verified Helper package");
        var workspace = Path.Combine(fixture, "workspace"); Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "Sample.cs"), "public class Sample { public int Number() => 42; }");
        var settings = Path.Combine(fixture, "helper.json");
        Environment.SetEnvironmentVariable("CONSTANTINE_HELPER_SETTINGS", settings);
        using var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        portProbe.Start(); var port = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        new HelperSettings(workspace, exe, port, false, HelperSettings.NewToken()).Save();
        var settingsBefore = File.ReadAllBytes(settings);
        using (var helper = new HelperAdapter())
        {
            await helper.StartAsync();
            Check((await helper.GetStatusAsync()).State == ConstantineHub.Core.AdapterState.Running, "Helper from upgraded installation passes real MCP probe");
        }
        // Launch only the isolated upgraded installation, never the user's Hub.
        var marker = Path.Combine(fixture, "startup-ok.txt");
        var start = new ProcessStartInfo(Path.Combine(target, "ConstantineHub.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = target };
        start.ArgumentList.Add("--update-ok-file"); start.ArgumentList.Add(marker);
        start.Environment["CONSTANTINE_HELPER_SETTINGS"] = settings;
        start.Environment["CONSTANTINE_GODOT_MCP_CONFIG"] = Path.Combine(fixture, "godot.json");
        start.Environment["CONSTANTINE_FILES_CONFIG"] = Path.Combine(fixture, "files.json");
        start.Environment["TUNNEL_CLIENT_PROFILE_DIR"] = fixture;
        // The existing Godot adapter requires a configured project at construction.
        // Supply an isolated fixture, just like the UI suite, rather than relying
        // on the developer workstation's fallback project on F:.
        File.WriteAllText(Path.Combine(fixture, "project.godot"), "[application]\nconfig/name=\"Upgrade fixture\"\n");
        var previousGodotConfig = Environment.GetEnvironmentVariable("CONSTANTINE_GODOT_MCP_CONFIG");
        try
        {
            Environment.SetEnvironmentVariable("CONSTANTINE_GODOT_MCP_CONFIG", start.Environment["CONSTANTINE_GODOT_MCP_CONFIG"]);
            GodotSettingsStore.Save(new GodotSettings(fixture, "upgrade-godot", "tunnel_upgrade", 8082,
                "127.0.0.1", 6262, "127.0.0.1", 6263, null, fixture, false));
        }
        finally { Environment.SetEnvironmentVariable("CONSTANTINE_GODOT_MCP_CONFIG", previousGodotConfig); }
        using (var hub = Process.Start(start) ?? throw new Exception("Cannot launch upgraded Hub"))
        {
            try
            {
                for (var i = 0; i < 80 && !File.Exists(marker) && !hub.HasExited; i++) await Task.Delay(150);
                Check(File.Exists(marker) && !hub.HasExited, "published upgraded Hub emits startup marker and remains alive");
            }
            finally
            {
                if (!hub.HasExited) { hub.CloseMainWindow(); if (!hub.WaitForExit(5000)) hub.Kill(entireProcessTree: true); }
                hub.WaitForExit(5000);
            }
        }
        Check(File.ReadAllBytes(settings).SequenceEqual(settingsBefore), "upgraded Hub startup preserves Helper user settings");
        Check(HubUpdaterProgram.RestoreBackup(target, backup, replaced, created, Path.Combine(fixture, "recovery.log")), "rollback restores real previous release");
        Check(Hashes(target).OrderBy(p => p.Key).SequenceEqual(original.OrderBy(p => p.Key)), $"rollback restores {previousVersion} files and removes newly created payload");
        Check(File.ReadAllBytes(settings).SequenceEqual(settingsBefore), "rollback preserves user configuration outside installation");
        Environment.SetEnvironmentVariable("CONSTANTINE_HELPER_SETTINGS", null);
        Console.WriteLine("RELEASE UPGRADE / RECOVERY PASS");
    }
    private static Dictionary<string, string> Hashes(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(root, p), p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
