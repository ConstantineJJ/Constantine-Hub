using ConstantineHub;
using System.Diagnostics;
using ConstantineHub.Adapters.Godot;
using ConstantineHub.Core;
using Tomlyn;
using Tomlyn.Model;

namespace ConstantineHub.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["codex", "plugin", "export", "--dir", var exportDirectory])
            return ExportFixture(exportDirectory);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            using var watchdog = new System.Threading.Timer(_ =>
            {
                Console.Error.WriteLine("FAIL: UI regression timed out after 60 seconds.");
                Environment.Exit(1);
            }, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
            TestConnections();
            TestPluginInstaller(args.Length == 2 && args[0] == "--plugin-client" ? args[1] : null);
            // A hosted CI runner has no workstation Godot project/configuration.
            var fixture = Path.Combine(AppContext.BaseDirectory, "smoke-fixture");
            Directory.CreateDirectory(fixture);
            File.WriteAllText(Path.Combine(fixture, "project.godot"), "[application]\nconfig/name=\"Hub smoke tests\"\n");
            Environment.SetEnvironmentVariable("CONSTANTINE_GODOT_MCP_CONFIG", Path.Combine(fixture, "godot.json"));
            Environment.SetEnvironmentVariable("CONSTANTINE_FILES_CONFIG", Path.Combine(fixture, "files.json"));
            Environment.SetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_DIR", fixture);
            Environment.SetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_FILE", null);
            GodotSettingsStore.Save(new GodotSettings(fixture, "smoke-godot", "tunnel_smoke", 8082,
                "127.0.0.1", 6262, "127.0.0.1", 6263, null, fixture, false));
            Console.WriteLine("Constructing UI");
            using var form = new MainForm { ShowInTaskbar = false };
            UiPass.Apply(form);
            UiAcceptancePass.Apply(form);
            Console.WriteLine("Showing UI");
            form.Show();
            Pump();
            Check(Descendants<Label>(form).Any(l => l.Text == "MCP CONNECTIONS"), "semantic connection card present");
            Check(!Descendants<Label>(form).Any(l => l.Text == "Needs attention"), "inactive bridges do not show generic program error");

            var starts = Descendants<Button>(form).Where(b => b.Text == "Start").ToArray();
            var legacy = starts.Where(b => b.Tag is EventHandler).ToArray();
            var headers = starts.Where(b => b.Tag is null).ToArray();
            Check(legacy.Length == 3 && headers.Length == 3, "three adapter Start actions");
            for (var i = 0; i < headers.Length; i++)
            {
                var calls = 0;
                legacy[i].Tag = new EventHandler((_, _) => calls++);
                Check(!legacy[i].Visible, "legacy Start hidden");
                headers[i].PerformClick();
                Check(calls == 1, "collapsed Start dispatches once");
                var toggle = headers[i].Parent!.Controls.OfType<Button>().Single(b => b.Text is "⌄" or "⌃");
                toggle.PerformClick();
                Pump();
                var actions = (FlowLayoutPanel)legacy[i].Parent!;
                Check(actions.Controls.OfType<Button>().Where(b => b.Visible)
                    .All(b => b.Bottom <= actions.ClientSize.Height), "expanded actions are not clipped");
                headers[i].PerformClick();
                Check(calls == 2, "expanded Start dispatches once");
                toggle.PerformClick();
                Pump();
            }

            var settings = Descendants<Button>(form).Single(b => b.Text == "Settings" && b.ContextMenuStrip is not null);
            var menu = settings.ContextMenuStrip!;
            var allowlist = Descendants<Button>(form).Single(b => b.Text == "Allowlist");
            var selected = 0;
            allowlist.Tag = new EventHandler((_, _) => selected++);
            for (var i = 0; i < 15; i++)
            {
                settings.PerformClick();
                Pump();
                Check(menu.Visible, "Settings menu opens");
                menu.Close();
                Pump();
                Check(!menu.IsDisposed && form.Visible, "closing Settings preserves menu and form");
            }
            settings.PerformClick();
            Pump();
            ((ToolStripMenuItem)menu.Items[0]).PerformClick();
            menu.Close();
            Pump();
            Check(selected == 1, "Settings allowlist action works in collapsed card after rename");

            var localInstall = Descendants<Button>(form).Single(b => b.Text == "Install Plugin" &&
                b.FindForm() == form && !HasGodotTitle(b));
            Check(localInstall.Tag is EventHandler, "Local Files install button is wired");
            var dispatched = 0;
            localInstall.Tag = new EventHandler((_, _) => dispatched++);
            MainForm.InvokeButtonAction(localInstall);
            Check(dispatched == 1, "plugin install action dispatches");
            form.Close();
            Pump();
            Check(menu.IsDisposed, "Settings menu disposed with owner");
            Console.WriteLine("PASS: all UI regressions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void TestConnections()
    {
        ServiceConnection[] services = [new("Local Files", AdapterState.External),
            new("Blender", AdapterState.Stopped, false), new("Godot editor", AdapterState.Stopped, false)];
        var overview = ConnectionOverview.Build(services);
        Check(overview.Title == "Bridges offline" && overview.Tone == ConnectionTone.Pending &&
            overview.Detail.Contains("Blender") && overview.Detail.Contains("Godot editor"),
            "ready Local Files with inactive engine bridges is informational and names both bridges");
        var allStopped = services.Select(s => s with { Tunnel = AdapterState.Stopped }).ToArray();
        Check(ConnectionOverview.Build(allStopped).Title == "Tunnels stopped", "intentionally stopped tunnels are not errors");
        Check(ConnectionOverview.Build(services.Select(s => s with { Tunnel = AdapterState.Running, BridgeConnected = true }).ToArray())
            .Tone == ConnectionTone.Ready, "connected bridges and tunnels are ready");
        Check(ConnectionOverview.Build([new("Local Files", AdapterState.Degraded)]).Tone == ConnectionTone.Error,
            "occupied or failed tunnel remains a real error");
        Check(ConnectionOverview.Build(services, new Dictionary<string, string> { ["Install Tunnel MCP plugin"] = "export failed" })
            .Detail.Contains("Install Tunnel MCP plugin"), "action failure names the failed operation");
        Check(ConnectionOverview.Build([new("Godot editor", AdapterState.Stopped, false, "Plugin missing")])
            .Title == "Setup required", "missing configuration has a separate status");
    }

    private static void TestPluginInstaller(string? realClient)
    {
        var executable = Path.GetFullPath(realClient ?? Environment.ProcessPath!);
        var home = Path.Combine(AppContext.BaseDirectory, "plugin-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        var config = Path.Combine(home, "config.toml");
        const string original = "# keep workstation settings\nmodel = \"keep-model\"\n" +
            "[plugins.\"other@debug\"]\nenabled = false\n" +
            "[plugins.\"tunnel-mcp@debug\"]\nenabled = false # preserve comment\ncustom = \"keep-option\"\n" +
            "[plugins.\"tunnel-mcp@debug\".permissions]\nnetwork = false\n";
        File.WriteAllText(config, original);
        var target = Path.Combine(home, "plugins", "cache", "debug", "tunnel-mcp", "local");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "previous.txt"), "old installation");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            TunnelPluginInstaller.InstallAsync(executable, home).GetAwaiter().GetResult();
            Check(File.ReadAllText(Path.Combine(target, ".tunnel-client-bin")).Trim() == executable,
                "Windows installer writes executable hint on attempt " + (attempt + 1));
            var text = File.ReadAllText(config);
            var model = Toml.ToModel(text);
            var plugins = (TomlTable)model["plugins"];
            var plugin = (TomlTable)plugins["tunnel-mcp@debug"];
            Check((bool)plugin["enabled"] && (string)plugin["custom"] == "keep-option" &&
                !(bool)((TomlTable)plugin["permissions"])["network"] &&
                !(bool)((TomlTable)plugins["other@debug"])["enabled"] &&
                (string)model["model"] == "keep-model" && text.Contains("keep workstation settings"),
                "install enables plugin and preserves unrelated settings, plugin options and permissions");
        }
        File.WriteAllText(Path.Combine(target, "previous.txt"), "keep installed plugin");
        var before = File.ReadAllText(config);
        if (realClient is null)
        {
            Environment.SetEnvironmentVariable("HUB_SMOKE_EXPORT_FAILURE", "1");
            try { ExpectInstallFailure(executable, home); }
            finally { Environment.SetEnvironmentVariable("HUB_SMOKE_EXPORT_FAILURE", null); }
            Check(File.ReadAllText(config) == before && File.Exists(Path.Combine(target, "previous.txt")),
                "unrelated export failure is not suppressed and preserves previous install");
        }
        // A blocked settings replacement must roll back the plugin directory too.
        using (var locked = File.Open(config, FileMode.Open, FileAccess.Read, FileShare.Read))
            ExpectInstallFailure(executable, home);
        Check(File.ReadAllText(config) == before && File.Exists(Path.Combine(target, "previous.txt")),
            "settings write failure restores previous plugin and leaves config untouched");
        File.WriteAllText(config, "invalid = [");
        ExpectInstallFailure(executable, home);
        Check(File.ReadAllText(config) == "invalid = [" && File.Exists(Path.Combine(target, "previous.txt")),
            "invalid TOML is reported without replacing existing files");
        Directory.Delete(home, recursive: true);
    }

    private static void ExpectInstallFailure(string executable, string home)
    {
        try { TunnelPluginInstaller.InstallAsync(executable, home).GetAwaiter().GetResult(); }
        catch { return; }
        throw new InvalidOperationException("Expected plugin installation failure.");
    }

    private static int ExportFixture(string directory)
    {
        if (Environment.GetEnvironmentVariable("HUB_SMOKE_EXPORT_FAILURE") == "1")
        {
            Console.Error.WriteLine("A different export error");
            return 2;
        }
        Directory.CreateDirectory(Path.Combine(directory, ".codex-plugin"));
        Directory.CreateDirectory(Path.Combine(directory, "mcp"));
        File.WriteAllText(Path.Combine(directory, ".codex-plugin", "plugin.json"), "{\"name\":\"tunnel-mcp\"}");
        File.WriteAllText(Path.Combine(directory, ".mcp.json"), "{\"mcpServers\":{\"tunnelMcp\":{}}}");
        File.WriteAllText(Path.Combine(directory, "mcp", "server.cjs"), "// Export test fixture");
        Console.Error.WriteLine("tunnel-client binary is not executable: " + Environment.ProcessPath);
        return 1;
    }

    private static bool HasGodotTitle(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not Panel) continue;
            var title = Descendants<Label>(parent).FirstOrDefault(l => l.Text is "GODOT MCP" or "LOCAL FILES MCP");
            if (title is not null) return title.Text == "GODOT MCP";
        }
        return false;
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Pump()
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 80)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
