using System.Drawing;

namespace ConstantineHub;

/// <summary>
/// Final v0.2.x live-acceptance polish applied after the base UI shell is built.
/// Keeps presentation-only corrections isolated from adapter/runtime behavior.
/// </summary>
internal static class UiAcceptancePass
{
    private static readonly Color Border = Color.FromArgb(49, 62, 74);
    private static readonly Color SurfaceHover = Color.FromArgb(31, 43, 55);
    private static readonly Color TextPrimary = Color.FromArgb(236, 239, 242);

    internal static void Apply(MainForm form)
    {
        var shell = form.Controls
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(panel => panel.ColumnCount == 3 && panel.RowCount == 2);

        if (shell is null)
            return;

        shell.ColumnStyles[0].SizeType = SizeType.Absolute;
        shell.ColumnStyles[0].Width = Scale(form, 220);
        shell.ColumnStyles[2].SizeType = SizeType.Absolute;
        shell.ColumnStyles[2].Width = Scale(form, 288);
        shell.RowStyles[0].SizeType = SizeType.Absolute;
        shell.RowStyles[0].Height = Scale(form, 84);

        PolishNavigation(form, shell.GetControlFromPosition(0, 0));
        PolishTopBar(shell.GetControlFromPosition(1, 0));
        PolishStatusRail(shell.GetControlFromPosition(2, 1));

        if (shell.GetControlFromPosition(1, 1) is FlowLayoutPanel dashboard)
        {
            PolishAdapter(form, dashboard, "LOCAL FILES MCP", "LocalFilesIcon.png", renameLocalSettings: true);
            PolishAdapter(form, dashboard, "BLENDER MCP", "BlenderIcon.png");
            PolishAdapter(form, dashboard, "GODOT MCP", "GodotIcon.png");
        }

        form.PerformLayout();
    }

    private static void PolishNavigation(MainForm form, Control? navigation)
    {
        if (navigation is null)
            return;

        var layout = Descendants<TableLayoutPanel>(navigation)
            .FirstOrDefault(panel => panel.RowCount >= 8 && panel.ColumnCount == 1);
        if (layout is null)
            return;

        CollapseNavRow(layout, "Updates", 4);
        CollapseNavRow(layout, "Settings", 6);

        if (layout.GetControlFromPosition(0, 0) is TableLayoutPanel brand)
        {
            if (brand.ColumnStyles.Count > 0)
            {
                brand.ColumnStyles[0].SizeType = SizeType.Absolute;
                brand.ColumnStyles[0].Width = Scale(form, 52);
            }

            var title = Descendants<Label>(brand)
                .FirstOrDefault(label => label.Text.Equals("Constantine", StringComparison.OrdinalIgnoreCase));
            if (title is not null)
            {
                title.Font = new Font("Segoe UI Semibold", 11F);
                title.AutoEllipsis = false;
                title.TextAlign = ContentAlignment.MiddleLeft;
            }
        }
    }

    private static void CollapseNavRow(TableLayoutPanel layout, string buttonText, int row)
    {
        var button = Descendants<Button>(layout)
            .FirstOrDefault(candidate => candidate.Text.Equals(buttonText, StringComparison.OrdinalIgnoreCase));
        if (button is not null)
            button.Visible = false;

        if (row >= 0 && row < layout.RowStyles.Count)
        {
            layout.RowStyles[row].SizeType = SizeType.Absolute;
            layout.RowStyles[row].Height = 0;
        }
    }

    private static void PolishTopBar(Control? topBar)
    {
        if (topBar is null)
            return;

        foreach (var label in Descendants<Label>(topBar))
        {
            if (label.Text.StartsWith("Profile:", StringComparison.OrdinalIgnoreCase) ||
                label.Text.StartsWith("Stable", StringComparison.OrdinalIgnoreCase))
            {
                label.Font = new Font("Segoe UI", 9F);
                label.AutoEllipsis = true;
            }
        }
    }

    private static void PolishStatusRail(Control? rail)
    {
        if (rail is null)
            return;

        foreach (var label in Descendants<Label>(rail))
        {
            if (label.Text.StartsWith("Runtime-idle", StringComparison.OrdinalIgnoreCase) ||
                label.Text.StartsWith("External / adopted", StringComparison.OrdinalIgnoreCase))
            {
                label.AutoEllipsis = false;
            }
        }
    }

    private static void PolishAdapter(
        MainForm form,
        FlowLayoutPanel dashboard,
        string title,
        string iconAsset,
        bool renameLocalSettings = false)
    {
        var host = dashboard.Controls
            .OfType<Panel>()
            .FirstOrDefault(panel => Descendants<Label>(panel)
                .Any(label => label.Text.Equals(title, StringComparison.OrdinalIgnoreCase)));
        if (host is null)
            return;

        var inner = host.Controls
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(panel => panel.RowCount == 2);
        if (inner is null)
            return;

        var header = inner.GetControlFromPosition(0, 0) as TableLayoutPanel;
        if (header is not null)
        {
            ReplaceServiceBadge(form, header, iconAsset);

            var headerStart = header.Controls
                .OfType<Button>()
                .FirstOrDefault(button => button.Text.Equals("Start", StringComparison.OrdinalIgnoreCase));

            foreach (var duplicateStart in Descendants<Button>(host)
                         .Where(button => button.Text.Equals("Start", StringComparison.OrdinalIgnoreCase) &&
                                          !ReferenceEquals(button, headerStart)))
            {
                duplicateStart.Visible = false;
            }
        }

        if (renameLocalSettings)
        {
            foreach (var localSettings in Descendants<Button>(host)
                         .Where(button => button.Text.Equals("Settings", StringComparison.OrdinalIgnoreCase)))
            {
                localSettings.Text = "Allowlist";
            }
        }

        // GetControlFromPosition skips the explicitly hidden collapsed body.
        var body = inner.Controls.OfType<Panel>().FirstOrDefault(panel => inner.GetRow(panel) == 1);
        if (body is null || body.Controls.Count == 0)
            return;

        var originalCard = body.Controls[0];
        var legacyLayout = originalCard.Controls
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(panel => panel.RowCount >= 3);
        if (legacyLayout is null)
            return;

        var actions = legacyLayout.GetControlFromPosition(0, 2) as FlowLayoutPanel;
        if (actions is not null)
        {
            legacyLayout.RowStyles[2].SizeType = SizeType.Absolute;
            legacyLayout.RowStyles[2].Height = Scale(form, 54);

            foreach (var button in actions.Controls.OfType<Button>())
            {
                button.MinimumSize = new Size(Scale(form, 94), Scale(form, 38));
                button.Height = Scale(form, 38);
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.BackColor = SurfaceHover;
                button.ForeColor = TextPrimary;
                button.Margin = new Padding(0, 0, Scale(form, 7), Scale(form, 5));
            }
        }

        var toggle = header is null
            ? null
            : header.Controls.OfType<Button>().FirstOrDefault(button => button.Text is "⌄" or "⌃");

        var adjusting = false;
        void EnsureExpandedControlsVisible()
        {
            if (adjusting || !body.Visible || form.IsDisposed || !form.IsHandleCreated)
                return;

            form.BeginInvoke(new Action(() =>
            {
                if (adjusting || form.IsDisposed || !body.Visible)
                    return;

                adjusting = true;
                try
                {
                    form.PerformLayout();
                    originalCard.PerformLayout();
                    if (actions is not null)
                    {
                        legacyLayout.RowStyles[2].Height = Math.Max(Scale(form, 54),
                            actions.GetPreferredSize(new Size(actions.ClientSize.Width, 0)).Height + actions.Margin.Vertical);
                        legacyLayout.PerformLayout();
                    }
                    actions?.PerformLayout();

                    var visibleButtons = actions?.Controls
                        .OfType<Button>()
                        .Where(button => button.Visible)
                        .ToArray() ?? Array.Empty<Button>();
                    if (visibleButtons.Length == 0)
                        return;

                    var maxButtonBottom = visibleButtons
                        .Select(button => button.RectangleToScreen(button.ClientRectangle).Bottom)
                        .Max();
                    var safeBottom = body.RectangleToScreen(body.ClientRectangle).Bottom - Scale(form, 10);
                    var overflow = maxButtonBottom - safeBottom;

                    if (overflow > 0)
                        host.Height += overflow + Scale(form, 8);
                }
                finally
                {
                    adjusting = false;
                }
            }));
        }

        body.VisibleChanged += (_, _) => EnsureExpandedControlsVisible();
        if (toggle is not null)
            toggle.Click += (_, _) => EnsureExpandedControlsVisible();

        EnsureExpandedControlsVisible();
    }

    private static void ReplaceServiceBadge(MainForm form, TableLayoutPanel header, string assetName)
    {
        var image = LoadAsset(assetName);
        if (image is null)
            return;

        var current = header.GetControlFromPosition(0, 0);
        if (current is PictureBox)
        {
            image.Dispose();
            return;
        }

        if (current is not null)
        {
            header.Controls.Remove(current);
            current.Dispose();
        }

        var icon = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Image = image,
            Margin = new Padding(0, 0, Scale(form, 8), 0),
            TabStop = false
        };
        header.Controls.Add(icon, 0, 0);
    }

    private static Image? LoadAsset(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "Assets", fileName)
        };

        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                using var source = Image.FromFile(path);
                return new Bitmap(source);
            }
            catch
            {
                // Optional UI art must never block Hub startup.
            }
        }

        return null;
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    private static int Scale(Control control, int logical)
        => Math.Max(1, (int)Math.Round(logical * Math.Max(96, control.DeviceDpi) / 96.0));
}
