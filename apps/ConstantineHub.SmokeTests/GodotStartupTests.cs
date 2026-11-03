using System.Text.Json.Nodes;
using ConstantineHub.Adapters.Godot;
using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class GodotStartupTests
{
    internal static void Run(string fixture)
    {
        var original = File.ReadAllText(GodotSettingsStore.ConfigPath);
        var moved = Path.Combine(fixture, "moved-project");
        Directory.CreateDirectory(moved);
        File.WriteAllText(Path.Combine(moved, "project.godot"), "[application]\nconfig/name=\"Moved project\"\n");
        var missing = Path.Combine(fixture, "previous-project-location");
        var notProject = Path.Combine(fixture, "not-a-project");
        Directory.CreateDirectory(notProject);
        try
        {
            foreach (var root in new[] { missing, notProject, "" })
            {
                var config = JsonNode.Parse(original)!;
                config["project_root"] = root;
                File.WriteAllText(GodotSettingsStore.ConfigPath, config.ToJsonString());
                var bytes = File.ReadAllBytes(GodotSettingsStore.ConfigPath);
                using var adapter = new GodotAdapter();
                var snapshot = Task.Run(() => adapter.GetSnapshotAsync()).GetAwaiter().GetResult();
                Check(!snapshot.Configured && !snapshot.EditorBridgeConnected && !snapshot.RuntimeBridgeConnected,
                    "unavailable or unselected project is reported without blocking Hub: " + root);
                Check(adapter.GetStatusAsync().GetAwaiter().GetResult().State == AdapterState.NotConfigured,
                    "Godot asks for project selection instead of reporting a startup failure");
                try { adapter.StartAsync().GetAwaiter().GetResult(); throw new Exception("Unavailable project was started"); }
                catch (Exception ex) when (ex is DirectoryNotFoundException or InvalidDataException) { }
                Check(!Directory.Exists(missing), "reading status or starting does not recreate the old project folder");
                using var form = new MainForm(enableTray: false);
                UiPass.Apply(form);
                UiAcceptancePass.Apply(form);
                Check(form.Controls.Find("AddGodotProject", true).Length == 1,
                    "Hub offers Add project with no usable active project");
                Check(File.ReadAllBytes(GodotSettingsStore.ConfigPath).SequenceEqual(bytes),
                    "opening Hub preserves the saved project choice");
                GodotProjectRegistry.Register(moved);
                adapter.SelectProjectAsync(moved).GetAwaiter().GetResult();
                Check(GodotSettingsStore.Load().ProjectRoot == moved,
                    "a valid moved project can be selected from an unavailable or empty previous choice");
                var filesConfig = ConstantineHub.Adapters.LocalFiles.LocalFilesSettingsStore.ConfigPath;
                var filesBefore = File.ReadAllText(filesConfig);
                try
                {
                    File.WriteAllText(filesConfig, new JsonObject
                    {
                        ["schema_version"] = 1,
                        ["allowed_roots"] = new JsonArray(new JsonObject
                        {
                            ["path"] = Path.Combine(fixture, "unavailable-unrelated-files-root"),
                            ["permissions"] = new JsonArray("read")
                        })
                    }.ToJsonString());
                    form.SelectGodotProjectAsync(moved).GetAwaiter().GetResult();
                    Check(GodotSettingsStore.Load().ProjectRoot == moved &&
                        form.UpdateLogTail.Contains("Local Files access needs attention"),
                        "project switching succeeds with a separate warning when an unrelated file root is unavailable");
                }
                finally { File.WriteAllText(filesConfig, filesBefore); }
                form.RequestExit();
            }
            File.Delete(GodotSettingsStore.ConfigPath);
            using var fresh = new MainForm(enableTray: false);
            UiPass.Apply(fresh);
            UiAcceptancePass.Apply(fresh);
            Check(GodotSettingsStore.Load().ProjectRoot == "" && !File.Exists(GodotSettingsStore.ConfigPath),
                "first launch needs no project and does not silently select a workstation fallback");
            fresh.RequestExit();
        }
        finally { File.WriteAllText(GodotSettingsStore.ConfigPath, original); }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
