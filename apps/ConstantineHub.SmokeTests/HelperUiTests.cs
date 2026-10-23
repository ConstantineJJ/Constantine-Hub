using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class HelperUiTests
{
    internal static void Run(MainForm form)
    {
        // Exercise the actual production modal, not a substituted button handler.
        var configurationFits = false;
        using (var timer = new System.Windows.Forms.Timer { Interval = 150 })
        {
            timer.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.Cast<Form>().SingleOrDefault(f => f.Text == "MCP Helper — workspace and package");
                if (dialog is null) return;
                timer.Stop();
                var save = Descendants<Button>(dialog).Single(b => b.Text == "Save");
                configurationFits = save.Visible && save.RectangleToScreen(save.ClientRectangle).Bottom <= dialog.RectangleToScreen(dialog.ClientRectangle).Bottom
                    && Descendants<Control>(dialog).Where(c => c.Visible).All(c => c.Right <= c.Parent!.ClientSize.Width && c.Bottom <= c.Parent.ClientSize.Height);
                var evidence = Environment.GetEnvironmentVariable("HUB_UI_EVIDENCE");
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    Directory.CreateDirectory(evidence);
                    using var bitmap = new Bitmap(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                    bitmap.Save(Path.Combine(evidence, "helper-configure.png"));
                }
                dialog.Close();
            };
            timer.Start();
            var task = (Task)typeof(MainForm).GetMethod("ConfigureHelperAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, null)!;
            while (!task.IsCompleted) Pump();
            task.GetAwaiter().GetResult();
        }
        Check(configurationFits, "R1 real Configure dialog and Save fit at native DPI");
        var filter = typeof(MainForm).GetMethod("IsVerboseTunnelLine", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        bool Verbose(string line) => (bool)filter.Invoke(null, [line])!;
        Check(Verbose("[time] MCP Helper: info: Microsoft.AspNetCore Request finished 200") &&
            Verbose("[time] MCP Helper: dbug: Transport details") &&
            !Verbose("[time] MCP Helper: warn: Slow request") &&
            !Verbose("[time] MCP Helper: fail: Request failed") &&
            !Verbose("[time] MCP Helper: Ready (Hub-owned)."), "R7 routine Helper diagnostics obey Verbose Off while warnings/errors/lifecycle remain visible");
        // Hosted desktops cap top-level windows at 1044px, even with explicit
        // MaximumSize. Exercise the same production shell in a sized child
        // viewport so both responsive modes are tested independently of the monitor.
        var scale = Math.Max(1f, form.DeviceDpi / 96f);
        var shell = form.Controls.OfType<TableLayoutPanel>().Single(p => p.ColumnCount == 3 && p.RowCount == 2);
        using var viewport = new Panel { Size = new Size((int)(1400 * scale), (int)(900 * scale)) };
        form.Controls.Remove(shell);
        form.Controls.Add(viewport);
        viewport.Controls.Add(shell);
        try
        {
            Pump();
            var panel = Descendants<TableLayoutPanel>(form).Single(p => p.Name == "McpHelperPanel");
            var split = panel.Parent as TableLayoutPanel ?? throw new Exception("Helper split is missing");
            var body = split.Parent!;
            var inner = (TableLayoutPanel)body.Parent!;
            var header = (TableLayoutPanel)inner.GetControlFromPosition(0, 0)!;
            var toggle = header.Controls.OfType<Button>().Single(b => b.Text is "⌄" or "⌃");
            if (!panel.Visible) toggle.PerformClick();
            Pump();
            Console.WriteLine($"Helper layout fixture: DPI={form.DeviceDpi}, window={form.Width}, viewport={viewport.Width}, body={body.Width}, row={split.GetCellPosition(panel).Row}, column={split.GetCellPosition(panel).Column}");
            Check(panel.Visible && split.GetCellPosition(panel).Column == 1, "Helper occupies requested right-hand Blender detail area");
            Check(Descendants<Label>(panel).Any(l => l.Text == "MCP HELPER"), "Helper has an independent title and controls");
            foreach (var button in Descendants<Button>(panel))
            {
                var original = button.Tag; var enabled = button.Enabled; var calls = 0;
                button.Tag = new EventHandler((_, _) => calls++); button.Enabled = true;
                button.PerformClick();
                Check(calls == 1, button.Name + " dispatches exactly once");
                button.Tag = original; button.Enabled = enabled;
            }
            form.UpdateHelperStatus(new("mcp-helper", "MCP Helper", AdapterState.External, "External test instance"));
            Check(Descendants<Button>(panel).Where(b => b.Name is "helper-start" or "helper-stop" or "helper-restart").All(b => !b.Enabled),
                "external Helper lifecycle actions remain disabled");
            var size = viewport.Size;
            viewport.Size = new Size((int)(1120 * scale), (int)(720 * scale)); Pump();
            Check(split.GetCellPosition(panel).Row == 1, "Helper stacks at narrow window width");
            Check(Descendants<Button>(panel).All(b => b.Width <= b.Parent!.ClientSize.Width), "Helper actions fit narrow layout");
            viewport.Size = size; Pump();
            Check(split.GetCellPosition(panel).Column == 1, "Helper returns to requested right-hand position after resize");
            Check(Descendants<Button>(panel).All(b => TextRenderer.MeasureText(b.Text, b.Font).Width + 8 <= b.ClientSize.Width),
                "complete Helper button labels fit at the native Windows DPI");
            var blender = split.GetControlFromPosition(0, 0)!;
            Check(Descendants<Button>(blender).Where(b => b.Visible).All(b =>
                b.RectangleToScreen(b.ClientRectangle).Bottom <= body.RectangleToScreen(body.ClientRectangle).Bottom),
                "Blender controls remain visible after Helper resize and reparenting");
            form.UpdateHelperStatus(new("mcp-helper", "MCP Helper", AdapterState.NotConfigured, "Choose a workspace"));
            var evidence = Environment.GetEnvironmentVariable("HUB_UI_EVIDENCE");
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                Directory.CreateDirectory(evidence);
                using var bitmap = new Bitmap(viewport.Width, viewport.Height);
                viewport.DrawToBitmap(bitmap, new Rectangle(Point.Empty, viewport.Size));
                bitmap.Save(Path.Combine(evidence, "hub-helper-wide.png"));
            }
        }
        finally
        {
            viewport.Controls.Remove(shell);
            form.Controls.Remove(viewport);
            form.Controls.Add(shell);
            form.PerformLayout();
        }
    }
    private static void Pump() { Application.DoEvents(); Thread.Sleep(30); Application.DoEvents(); }
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (var match2 in Descendants<T>(child)) yield return match2;
        }
    }
}
