using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class HelperUiTests
{
    internal static void Run(MainForm form)
    {
        // Hosted Windows runners can clamp the initial window to a small desktop.
        // Give this explicit wide-layout fixture its own tracking bounds.
        var scale = Math.Max(1f, form.DeviceDpi / 96f);
        form.MaximumSize = new Size((int)(2400 * scale), (int)(1800 * scale));
        form.Size = new Size((int)(1600 * scale), (int)(900 * scale));
        Pump();
        var panel = Descendants<TableLayoutPanel>(form).Single(p => p.Name == "McpHelperPanel");
        var split = panel.Parent as TableLayoutPanel ?? throw new Exception("Helper split is missing");
        var body = split.Parent!;
        var inner = (TableLayoutPanel)body.Parent!;
        var header = (TableLayoutPanel)inner.GetControlFromPosition(0, 0)!;
        var toggle = header.Controls.OfType<Button>().Single(b => b.Text is "⌄" or "⌃");
        if (!panel.Visible) toggle.PerformClick();
        Pump();
        Console.WriteLine($"Helper layout fixture: DPI={form.DeviceDpi}, window={form.Width}, body={body.Width}, row={split.GetCellPosition(panel).Row}, column={split.GetCellPosition(panel).Column}");
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
        var size = form.Size;
        form.Size = form.MinimumSize; Pump();
        Check(split.GetCellPosition(panel).Row == 1, "Helper stacks at narrow window width");
        Check(Descendants<Button>(panel).All(b => b.Width <= b.Parent!.ClientSize.Width), "Helper actions fit narrow layout");
        form.Size = size; Pump();
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
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(evidence, "hub-helper-wide.png"));
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
