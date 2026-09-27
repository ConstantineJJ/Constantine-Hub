using ConstantineHub;
using System.Drawing.Imaging;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Deterministic physical-pixel/font simulation on both desktop and Windows CI.
        // Real monitor transitions remain a separate manual acceptance gate.
        var native = args.Contains("--native");
        Application.SetHighDpiMode(native ? HighDpiMode.PerMonitorV2 : HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var output = Path.GetFullPath(args.FirstOrDefault(a => a != "--native") ?? "artifacts/layout");
        Directory.CreateDirectory(output);
        try
        {
            if (native)
            {
                CheckLayout(96, 15, 1280, output, native: true);
                Console.WriteLine("PASS: native monitor DPI layout.");
                return 0;
            }
            foreach (var dpi in new[] { 96, 120, 144, 192 })
            foreach (var points in new[] { 15F, 18F, 22.5F })
            foreach (var width in new[] { 900, 1280 })
                CheckLayout(dpi, points, width, output);
            Console.WriteLine("PASS: 24 layouts; 100/125/150/200% DPI simulation, 15/18/22.5 pt, narrow/wide.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CheckLayout(int dpi, float points, int width, string output, bool native = false)
    {
        using var form = new MainForm(layoutPreview: true);
        var scale = dpi / 96F;
        form.Font = new Font("Segoe UI", points);
        form.Size = new Size(width, width == 900 ? 900 : 1200);
        var fonts = Descendants(form).Prepend(form).Select(c => (Control: c, Font: c.Font)).ToArray();
        if (!native)
        {
            form.Scale(new SizeF(scale, scale));
            foreach (var entry in fonts)
                entry.Control.Font = new Font(entry.Font.FontFamily, entry.Font.SizeInPoints * scale, entry.Font.Style);
        }
        // Create native handles and settle layout without showing a tray icon or starting adapters.
        _ = form.Handle;
        foreach (var control in Descendants(form)) _ = control.Handle;
        Settle(form);
        var labels = Descendants(form).OfType<Label>().ToArray();
        var values = labels.Where(l => l.Text == "● Checking…").ToArray();
        Require(values.Length == 13, "Missing status rows (expected 3 + 4 + 6).");
        var samples = new[]
        {
            "● Tunnel ready (Hub-owned)", "● 3 configured", "● Read • Write ON • Delete ON",
            "● Running", "● Connected 127.0.0.1:9876", "● Tunnel ready (external/adopted)", "● 8/8 verified",
            @"● F:\My Lab\my-lab-4-exp", "● Plugin installed and enabled", "● Connected 127.0.0.1:6262",
            "● Stopped / not connected", "● Tunnel ready (Hub-owned)", "● Canonical Tools_C contracts verified"
        };
        for (var i = 0; i < values.Length; i++)
            values[i].Text = samples[i];
        Settle(form);
        var log = Descendants(form).OfType<RichTextBox>().Single();
        var logHeight = log.Height;
        Require(Math.Abs(log.Font.SizeInPoints - 9.5F * scale) < .1F, "Log font changed with UI font.");
        VerifyControls(form, dpi, points, width);

        // A later status refresh must resize the card, not hide subsequent rows or shrink the log.
        values[7].Text = "● " + string.Join(" ", Enumerable.Repeat(@"F:\A longer project directory\with spaces\", 6));
        Settle(form);
        VerifyControls(form, dpi, points, width);
        Require(log.Height == logHeight, "Status growth changed log viewport height.");
        values[7].Text = samples[7];
        Settle(form);

        var viewport = Descendants(form).OfType<Panel>().Single(p => p.GetType() == typeof(Panel) && p.AutoScroll);
        var capture = points == 15 && width == 1280 && (dpi == 96 || dpi == 144);
        var prefix = native ? $"hub-native-{form.DeviceDpi}dpi" : $"hub-{dpi}dpi";
        if (capture)
        {
            viewport.AutoScrollPosition = Point.Empty;
            Settle(form);
            Capture(form, Path.Combine(output, $"{prefix}-top.png"));
        }
        var lastCard = labels.Single(l => l.Text == "GODOT MCP").Parent!;
        viewport.ScrollControlIntoView(lastCard);
        Settle(form);
        var lastRow = values[^1];
        var rowOnScreen = lastRow.RectangleToScreen(lastRow.ClientRectangle);
        var viewOnScreen = viewport.RectangleToScreen(viewport.ClientRectangle);
        // A tall card can exceed the viewport; scrolling to the final row must still work.
        viewport.AutoScrollPosition = new Point(0, viewport.DisplayRectangle.Height);
        Settle(form);
        rowOnScreen = lastRow.RectangleToScreen(lastRow.ClientRectangle);
        Require(rowOnScreen.Bottom <= viewOnScreen.Bottom, "Last Godot row is unreachable by scrolling.");

        if (capture) Capture(form, Path.Combine(output, $"{prefix}-bottom.png"));
        if (native)
        {
            form.Font = new Font("Segoe UI", 22.5F);
            form.Size = new Size(900, 900);
            Settle(form);
            VerifyControls(form, form.DeviceDpi, 22.5F, 900);
            Require(log.Height == logHeight, "Live font/width change resized the log.");
        }
        Console.WriteLine($"PASS dpi={dpi} font={points} width={width} log={log.Height}px");
    }

    private static void Capture(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void VerifyControls(Form form, int dpi, float points, int width)
    {
        foreach (var control in Descendants(form).Where(c => c is Label or Button))
        {
            var preferred = control.GetPreferredSize(new Size(control.Width, 0));
            var context = $"dpi={dpi} font={points} width={width}: {control.Text}";
            Require(control.Height >= preferred.Height, $"Text clipped: {context}; actual={control.Size} preferred={preferred}");
            if (control is Button)
                Require(control.Width >= preferred.Width, $"Button text clipped: {context}");
            // Check every ancestor inside the scrolled content, not only the label's own bounds.
            var bounds = control.RectangleToScreen(control.ClientRectangle);
            for (var parent = control.Parent; parent is not null && parent is not Form; parent = parent.Parent)
            {
                if (parent is Panel { AutoScroll: true }) break;
                var container = parent.RectangleToScreen(parent.ClientRectangle);
                Require(bounds.Top >= container.Top && bounds.Bottom <= container.Bottom,
                    $"Ancestor clips text: {context}; {parent.GetType().Name} {container} vs {bounds}");
            }
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Settle(Control root)
    {
        for (var i = 0; i < 2; i++)
        {
            root.PerformLayout();
            Application.DoEvents();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
