using System.Text.Json.Nodes;
using System.Diagnostics;
using ConstantineHub;
using ConstantineHub.Adapters.LocalFiles;

namespace ConstantineHub.SmokeTests;

internal static class LocalFilesRecoveryTests
{
    internal static void Run(string fixture)
    {
        var original = File.ReadAllText(LocalFilesSettingsStore.ConfigPath);
        var available = Path.Combine(fixture, "recovery-available");
        var previous = Path.Combine(fixture, "recovery-before-move");
        var moved = Path.Combine(fixture, "recovery-after-move");
        Directory.CreateDirectory(available);
        Directory.CreateDirectory(previous);
        File.WriteAllText(Path.Combine(previous, "keep.txt"), "asset stays intact");
        Directory.Move(previous, moved);
        var document = new JsonObject
        {
            ["schema_version"] = 1,
            ["allowed_roots"] = new JsonArray(
                Rule(available, "read"), Rule(previous, "read", "write", "delete")),
            ["limits"] = new JsonObject { ["max_read_bytes"] = 12345 },
            ["audit_log"] = "unchanged-audit.jsonl"
        };
        try
        {
            File.WriteAllText(LocalFilesSettingsStore.ConfigPath, document.ToJsonString());
            var summary = LocalFilesSettingsStore.GetSummary();
            Check(!summary.ConfigValid && summary.RootCount == 2,
                "an unavailable root is reported while retaining all allowlist entries for recovery");
            var rules = LocalFilesSettingsStore.LoadRules(requireAvailable: false);
            Check(rules.Count == 2 && rules[1].Read && rules[1].Write && rules[1].Delete,
                "editing loads missing paths with their original permissions");
            try { LocalFilesSettingsStore.LoadRules(); throw new Exception("Missing folder accepted by runtime"); }
            catch (DirectoryNotFoundException) { }
            using (var settings = new LocalFilesSettingsForm())
            {
                settings.Size = settings.MinimumSize;
                settings.Show();
                Application.DoEvents();
                var grid = settings.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<DataGridView>().Single();
                var buttons = settings.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().ToArray();
                Check(buttons.All(button => button.Right <= button.Parent!.ClientSize.Width && button.Bottom <= button.Parent.ClientSize.Height &&
                    TextRenderer.MeasureText(button.Text, button.Font).Width < button.ClientSize.Width),
                    "allowlist actions fit the minimum window and show their complete captions at native DPI");
                Check(grid.Rows.Count == 2 && grid.Rows[1].Cells["Availability"].Value?.ToString() == "Недоступна",
                    "settings opens with all rows and marks unavailable folders");
                settings.ReplaceFolder(1, moved);
                Check(grid.Rows[1].Cells["Path"].Value?.ToString() == moved &&
                    Convert.ToBoolean(grid.Rows[1].Cells["Read"].Value) &&
                    Convert.ToBoolean(grid.Rows[1].Cells["Write"].Value) && Convert.ToBoolean(grid.Rows[1].Cells["Delete"].Value),
                    "relocation changes the selected path while retaining all permissions");
                try { settings.ReplaceFolder(1, available); throw new Exception("Duplicate replacement accepted"); }
                catch (InvalidDataException) { }
                Check(settings.RemoveUnavailableRows() == 0, "relocated folders are not removed as unavailable");
            }
            LocalFilesSettingsStore.SaveRules([rules[0], rules[1] with { Path = moved }]);
            var saved = JsonNode.Parse(File.ReadAllText(LocalFilesSettingsStore.ConfigPath))!;
            Check(LocalFilesSettingsStore.GetSummary().ConfigValid && saved["limits"]!["max_read_bytes"]!.GetValue<int>() == 12345 &&
                saved["audit_log"]!.GetValue<string>() == "unchanged-audit.jsonl",
                "saving relocated paths preserves limits and audit settings");
            Check(JsonNode.Parse(File.ReadAllText(LocalFilesSettingsStore.ConfigPath + ".bak"))!["allowed_roots"]![1]!["path"]!.GetValue<string>() == previous,
                "repair keeps one backup of the previous allowlist");

            File.WriteAllText(LocalFilesSettingsStore.ConfigPath, document.ToJsonString());
            Check(LocalFilesSettingsStore.RemoveUnavailableRoots() == 1 && LocalFilesSettingsStore.LoadRules().Single() == rules[0],
                "cleanup removes only unavailable entries and preserves available permissions");
            Check(File.ReadAllText(Path.Combine(moved, "keep.txt")) == "asset stays intact" && !Directory.Exists(previous),
                "allowlist repair does not delete assets or recreate the old directory");

            File.WriteAllText(LocalFilesSettingsStore.ConfigPath, document.ToJsonString());
            Directory.CreateDirectory(previous);
            var beforeRecheck = File.ReadAllBytes(LocalFilesSettingsStore.ConfigPath);
            Check(LocalFilesSettingsStore.RemoveUnavailableRoots() == 0 &&
                File.ReadAllBytes(LocalFilesSettingsStore.ConfigPath).SequenceEqual(beforeRecheck),
                "a folder that becomes available again is retained without rewriting the config");
            Directory.Move(previous, previous + "-returned");
            LocalFilesSettingsStore.SaveRules([rules[1]], requireAvailable: false);
            Check(LocalFilesSettingsStore.RemoveUnavailableRoots() == 1 &&
                LocalFilesSettingsStore.LoadRules(requireAvailable: false).Count == 0 && !LocalFilesSettingsStore.GetSummary().ConfigValid,
                "removing every unavailable entry saves an empty allowlist with no implicit access");
            using (var adapter = new LocalFilesAdapter())
            {
                try { adapter.StartAsync().GetAwaiter().GetResult(); throw new Exception("Empty allowlist started"); }
                catch (InvalidDataException) { }
            }
            LocalFilesSettingsStore.SaveRules([rules[0]]);
            Check(LocalFilesSettingsStore.GetSummary().ConfigValid, "an empty allowlist can be repaired by adding a folder");

            File.WriteAllText(LocalFilesSettingsStore.ConfigPath, document.ToJsonString());
            using var hub = new MainForm(enableTray: false);
            UiPass.Apply(hub);
            UiAcceptancePass.Apply(hub);
            hub.Show();
            Application.DoEvents();
            var notice = hub.Controls.Find("AllowlistRecoveryNotice", true).Single();
            Check(notice.Visible && hub.Controls.Find("RemoveUnavailableAllowlistPaths", true).Length == 1 &&
                hub.Controls.Find("ConfigureUnavailableAllowlistPaths", true).Length == 1,
                "Hub shows an actionable notification with cleanup and settings choices");
            ((Button)hub.Controls.Find("DismissAllowlistNotice", true).Single()).PerformClick();
            hub.RefreshAllowlistNotification();
            Check(!notice.Visible, "Later suppresses repeated notifications for the same unavailable folders");
            LocalFilesSettingsStore.SaveRules([rules[0]]);
            hub.RefreshAllowlistNotification();
            File.WriteAllText(LocalFilesSettingsStore.ConfigPath, document.ToJsonString());
            hub.RefreshAllowlistNotification();
            Check(notice.Visible, "an issue that returns after recovery is announced again");
            ((Button)hub.Controls.Find("DismissAllowlistNotice", true).Single()).PerformClick();
            ((JsonArray)document["allowed_roots"]!).Add(Rule(previous + "-another-missing", "read"));
            File.WriteAllText(LocalFilesSettingsStore.ConfigPath, document.ToJsonString());
            hub.RefreshAllowlistNotification();
            Check(notice.Visible, "new unavailable paths produce a fresh notification");
            var cleanup = hub.RemoveUnavailablePathsAsync();
            var timeout = Stopwatch.StartNew();
            while (!cleanup.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(10); }
            Check(cleanup.IsCompleted, "notification cleanup completes");
            cleanup.GetAwaiter().GetResult();
            hub.RefreshAllowlistNotification();
            Check(!notice.Visible && LocalFilesSettingsStore.LoadRules().Single() == rules[0],
                "notification cleanup removes invalid entries and clears after recovery");
            File.WriteAllText(LocalFilesSettingsStore.ConfigPath, "{\"schema_version\":\"invalid\",\"allowed_roots\":[]}");
            hub.RefreshAllowlistNotification();
            Check(!notice.Visible, "an invalid config value cannot crash the notification or offer destructive cleanup");
            hub.RequestExit();
            document["allowed_roots"] = new JsonArray(Rule(previous, "unsupported-permission"));
            var invalidConfig = document.ToJsonString();
            File.WriteAllText(LocalFilesSettingsStore.ConfigPath, invalidConfig);
            try { LocalFilesSettingsStore.RemoveUnavailableRoots(); throw new Exception("Malformed entry was discarded"); }
            catch (InvalidDataException) { }
            Check(File.ReadAllText(LocalFilesSettingsStore.ConfigPath) == invalidConfig,
                "cleanup refuses structurally invalid permissions without rewriting the config");
        }
        finally { File.WriteAllText(LocalFilesSettingsStore.ConfigPath, original); }
    }

    private static JsonObject Rule(string path, params string[] permissions) => new()
    {
        ["path"] = path,
        ["permissions"] = new JsonArray(permissions.Select(p => JsonValue.Create(p)).ToArray())
    };

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
