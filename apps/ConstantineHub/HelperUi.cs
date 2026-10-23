using System.Text.Json;
using ConstantineHub.Adapters.Helper;
using ConstantineHub.Core;

namespace ConstantineHub;

internal sealed partial class MainForm
{
    private readonly HelperAdapter _helper = new();
    private readonly Label _helperStatus = new() { Text = "Choose a workspace", AutoEllipsis = true, Dock = DockStyle.Fill };
    private readonly Label _helperWorkspace = new() { Text = "Independent C# analysis • read-only", AutoEllipsis = true, Dock = DockStyle.Fill };
    private Button? _helperStart, _helperStop, _helperRestart, _helperConfigure;
    private TableLayoutPanel? _helperPanel;

    internal void AttachHelperPanel()
    {
        if (_helperPanel is not null) return;
        var title = HelperDescendants<Label>(this).FirstOrDefault(l => l.Text == "BLENDER MCP" && l.Parent is TableLayoutPanel { ColumnCount: 5 });
        if (title?.Parent?.Parent is not TableLayoutPanel inner) return;
        var body = inner.Controls.OfType<Panel>().FirstOrDefault(p => inner.GetRow(p) == 1);
        if (body is null || body.Controls.Count != 1) return;
        var original = body.Controls[0];
        // Keep Blender's status rows/actions anchored at the top of its half.
        // The old percent row stretched the final status and pushed actions out
        // after the body was reparented into a responsive split layout.
        int S(int value) => (int)Math.Round(value * Math.Max(1f, DeviceDpi / 96f));
        var legacy = original.Controls.OfType<TableLayoutPanel>().First();
        var statuses = legacy.Controls.OfType<TableLayoutPanel>().First(p => legacy.GetRow(p) == 1);
        foreach (RowStyle row in statuses.RowStyles) { row.SizeType = SizeType.Absolute; row.Height = S(36); }
        legacy.RowStyles[1].SizeType = SizeType.Absolute;
        legacy.RowStyles[1].Height = statuses.RowCount * S(36) + statuses.Padding.Vertical;
        legacy.RowCount = 4;
        legacy.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Remove(original);
        var split = new TableLayoutPanel { Name = "HelperSplit", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            BackColor = Color.FromArgb(37, 48, 59), Margin = Padding.Empty, Padding = Padding.Empty };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _helperPanel = BuildHelperPanel();
        split.Controls.Add(original, 0, 0);
        split.Controls.Add(_helperPanel, 1, 0);
        body.Controls.Add(split);
        var host = inner.Parent!;
        var resizing = false;
        bool? wasStacked = null;
        void Fit()
        {
            if (resizing || IsDisposed) return;
            resizing = true;
            try
            {
                var scale = Math.Max(1f, DeviceDpi / 96f);
                var stacked = body.ClientSize.Width < 640 * scale;
                var changed = wasStacked != stacked;
                wasStacked = stacked;
                split.SuspendLayout();
                split.SetCellPosition(_helperPanel, stacked ? new TableLayoutPanelCellPosition(0, 1) : new TableLayoutPanelCellPosition(1, 0));
                split.SetColumnSpan(original, stacked ? 2 : 1);
                split.SetColumnSpan(_helperPanel, stacked ? 2 : 1);
                split.RowStyles[0].SizeType = stacked ? SizeType.Absolute : SizeType.Percent;
                split.RowStyles[0].Height = stacked ? 210 * scale : 100;
                split.RowStyles[1].SizeType = stacked ? SizeType.Percent : SizeType.Absolute;
                split.RowStyles[1].Height = stacked ? 100 : 0;
                if (body.Visible && changed) host.Height = (int)((stacked ? 510 : 350) * scale);
                split.ResumeLayout(true);
            }
            finally { resizing = false; }
        }
        body.SizeChanged += (_, _) => Fit();
        body.VisibleChanged += (_, _) => { wasStacked = null; Fit(); };
        Fit();
        UpdateHelperStatus(new("mcp-helper", "MCP Helper", AdapterState.NotConfigured, "Choose a workspace"));
    }

    private TableLayoutPanel BuildHelperPanel()
    {
        int S(int value) => (int)Math.Round(value * Math.Max(1f, DeviceDpi / 96f));
        var panel = new TableLayoutPanel { Name = "McpHelperPanel", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(S(12), S(5), S(8), S(6)), Margin = Padding.Empty, BackColor = Color.FromArgb(37, 48, 59), Font = new Font("Segoe UI", 9F) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, S(26)));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, S(30)));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, S(34)));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = "MCP HELPER", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(255, 162, 49),
            Font = new Font("Segoe UI Semibold", 11F), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        panel.Controls.Add(_helperStatus, 0, 1);
        _helperWorkspace.ForeColor = Color.FromArgb(155, 175, 191);
        panel.Controls.Add(_helperWorkspace, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true, Margin = Padding.Empty };
        Button Action(string text, string name, EventHandler action, bool primary = false)
        {
            var button = new Button { Name = name, Text = text, Width = S(108), Height = S(34), FlatStyle = FlatStyle.Flat,
                Font = panel.Font, Margin = new Padding(0, 0, S(5), S(6)), ForeColor = primary ? Color.FromArgb(30, 34, 40) : Color.Gainsboro,
                BackColor = primary ? Color.FromArgb(255, 162, 49) : Color.FromArgb(46, 60, 74), Tag = action };
            button.FlatAppearance.BorderColor = Color.FromArgb(55, 71, 87);
            button.Click += (_, _) => InvokeButtonAction(button);
            actions.Controls.Add(button);
            return button;
        }
        _helperStart = Action("Start Helper", "helper-start", async (_, _) => await RunAdapterActionAsync("Start MCP Helper", _helper.StartAsync), true);
        _helperStop = Action("Stop Helper", "helper-stop", async (_, _) => await RunAdapterActionAsync("Stop MCP Helper", _helper.StopAsync));
        _helperRestart = Action("Restart Helper", "helper-restart", async (_, _) => await RunAdapterActionAsync("Restart MCP Helper", _helper.RestartAsync));
        Action("Doctor", "helper-doctor", async (_, _) =>
        {
            try { MessageBox.Show(this, await _helper.DoctorAsync(), "MCP Helper Doctor", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            catch (Exception ex) { ErrorReporter.Show(ex, "MCP Helper Doctor", this); }
        });
        _helperConfigure = Action("Configure", "helper-configure", async (_, _) => await ConfigureHelperAsync());
        Action("Connection", "helper-connection", (_, _) =>
        {
            try
            {
                var settings = HelperSettings.Load(); settings.Validate();
                Clipboard.SetText(JsonSerializer.Serialize(new { url = $"http://127.0.0.1:{settings.Port}/mcp",
                    headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + settings.Token } }, new JsonSerializerOptions { WriteIndented = true }));
                MessageBox.Show(this, "MCP endpoint and authentication copied for your local MCP client.", "MCP Helper connection");
            }
            catch (Exception ex) { ErrorReporter.Show(ex, "MCP Helper connection", this); }
        });
        panel.Controls.Add(actions, 0, 3);
        return panel;
    }

    internal void UpdateHelperStatus(AdapterStatus status)
    {
        SetState(_helperStatus, status.State, status.Summary);
        if (status.Details?.TryGetValue("Workspace", out var root) == true) _helperWorkspace.Text = root;
        if (_helperStart is null) return;
        _helperStart.Enabled = status.State is AdapterState.Stopped or AdapterState.Degraded;
        _helperStop!.Enabled = _helperRestart!.Enabled = _helper.OwnsProcess && status.State != AdapterState.External;
        _helperConfigure!.Enabled = !_helper.OwnsProcess;
    }

    private async Task RefreshHelperAsync() => UpdateHelperStatus(await _helper.GetStatusAsync());
    private static bool HelperInGlobalActions()
    {
        try { return HelperSettings.Load().IncludeInStartAll; }
        catch { return false; }
    }

    private async Task ConfigureHelperAsync()
    {
        if (_helper.OwnsProcess) return;
        HelperSettings settings;
        try { settings = HelperSettings.Load(); }
        catch (Exception ex) { ErrorReporter.Show(ex, "MCP Helper settings", this); return; }
        using var dialog = new Form { Text = "MCP Helper — workspace and package", Width = 660,
            AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(660, 0),
            StartPosition = FormStartPosition.CenterParent, Font = new Font("Segoe UI", 10F), MinimizeBox = false, MaximizeBox = false };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(14), ColumnCount = 3, RowCount = 6 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < 6; row++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var workspace = new TextBox { Text = settings.WorkspaceRoot, Dock = DockStyle.Fill };
        var executable = new TextBox { Text = settings.Executable, PlaceholderText = "Bundled MCP Helper", Dock = DockStyle.Fill };
        var port = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = Math.Clamp(settings.Port, 1024, 65535), Width = 110 };
        var globals = new CheckBox { Text = "Include in global Start / Stop actions", Checked = settings.IncludeInStartAll, AutoSize = true };
        void Row(string name, Control input, int row) { grid.Controls.Add(new Label { Text = name, AutoSize = true }, 0, row); grid.Controls.Add(input, 1, row); }
        Row("Workspace", workspace, 0); Row("Package exe", executable, 1); Row("Local port", port, 2);
        var chooseRoot = new Button { Text = "Browse", AutoSize = true };
        chooseRoot.Click += (_, _) => { using var browse = new FolderBrowserDialog { Description = "Choose the C# project workspace" }; if (browse.ShowDialog(dialog) == DialogResult.OK) workspace.Text = browse.SelectedPath; };
        grid.Controls.Add(chooseRoot, 2, 0);
        var chooseExe = new Button { Text = "Browse", AutoSize = true };
        chooseExe.Click += (_, _) => { using var browse = new OpenFileDialog { Filter = "MCP Helper|McpHelper.exe" }; if (browse.ShowDialog(dialog) == DialogResult.OK) executable.Text = browse.FileName; };
        grid.Controls.Add(chooseExe, 2, 1);
        grid.Controls.Add(globals, 1, 3); grid.SetColumnSpan(globals, 2);
        var hint = new Label { Text = "Helper reads C# files without running project builds.\nSave, then Start Helper. Connection copies your MCP client settings.", AutoSize = true, MaximumSize = new Size(560, 0) };
        grid.Controls.Add(hint, 0, 4); grid.SetColumnSpan(hint, 3);
        var save = new Button { Text = "Save", AutoSize = true };
        save.Click += async (_, _) =>
        {
            save.Enabled = false;
            try
            {
                var next = new HelperSettings(workspace.Text.Trim(), executable.Text.Trim(), (int)port.Value, globals.Checked,
                    string.IsNullOrEmpty(settings.Token) ? HelperSettings.NewToken() : settings.Token);
                next.Validate();
                await Task.Run(() => HelperPackage.Verify(next.ExecutablePath));
                next.Save(); dialog.DialogResult = DialogResult.OK; dialog.Close();
            }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "MCP Helper settings", MessageBoxButtons.OK, MessageBoxIcon.Warning); save.Enabled = true; }
        };
        grid.Controls.Add(save, 2, 5); dialog.Controls.Add(grid); dialog.ShowDialog(this);
        await RefreshHelperAsync();
    }

    private static IEnumerable<T> HelperDescendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (var descendant in HelperDescendants<T>(child)) yield return descendant;
        }
    }
}
