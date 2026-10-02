using ConstantineHub;
using System.Diagnostics;
using ConstantineHub.Adapters.Godot;

namespace ConstantineHub.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
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
            using var form = new MainForm { ShowInTaskbar = false };
            UiPass.Apply(form);
            UiAcceptancePass.Apply(form);
            form.Show();
            Pump();

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
