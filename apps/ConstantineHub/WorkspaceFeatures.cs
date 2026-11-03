using System.Collections.Concurrent;
using System.Diagnostics;
using ConstantineHub.Adapters.Blender;
using ConstantineHub.Adapters.Godot;
using ConstantineHub.Core;

namespace ConstantineHub;

internal sealed partial class MainForm
{
    private readonly Dictionary<string, (Label Status, Button Stop)> _bridgeRows = new();
    private readonly ConcurrentQueue<string> _pendingLogs = new();
    private readonly System.Windows.Forms.Timer _logTimer = new() { Interval = 300 };
    private ComboBox? _projectPicker;
    private bool _changingProject;
    private AdapterStatus? _latestHelper;
    private int _pendingLogCount;

    private void QueueLog(string message)
    {
        if (Interlocked.Increment(ref _pendingLogCount) > 1000)
        { Interlocked.Decrement(ref _pendingLogCount); return; }
        _pendingLogs.Enqueue(message);
    }

    internal void AttachWorkspaceFeatures()
    {
        int S(int value) => (int)Math.Round(value * Math.Max(1f, DeviceDpi / 96f));
        var shell = Controls.OfType<TableLayoutPanel>().First(p => p.ColumnCount == 3);
        var rail = shell.GetControlFromPosition(2, 1)!;
        var flow = rail.Controls.OfType<FlowLayoutPanel>().First();
        var services = new TableLayoutPanel { Name = "ActiveBridges", ColumnCount = 1, RowCount = 5,
            BackColor = Color.FromArgb(18, 26, 34), Height = S(310), Padding = new Padding(S(10)), Margin = new Padding(0, 0, 0, S(10)) };
        services.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        services.RowStyles.Add(new RowStyle(SizeType.Absolute, S(28)));
        services.Controls.Add(new Label { Text = "ACTIVE MCP BRIDGES", Dock = DockStyle.Fill, ForeColor = Warn, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        var names = new[] { ("local-files", "Local Files"), ("blender", "Blender"), ("godot", "Godot"), ("mcp-helper", "MCP Helper") };
        foreach (var (id, name) in names)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(64)));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, S(25)));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            var stop = MakeButton("Stop", async (_, _) => await StopBridgeAsync(id));
            stop.Name = "rail-stop-" + id; stop.Dock = DockStyle.Fill; stop.Margin = new Padding(2); stop.Enabled = false; stop.Font = new Font("Segoe UI", 9F);
            row.Controls.Add(stop, 1, 0); row.SetRowSpan(stop, 2);
            var status = new Label { Text = "Checking…", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Muted, Font = new Font("Segoe UI", 8.5F) };
            row.Controls.Add(status, 0, 1);
            _bridgeRows[id] = (status, stop);
            _tunnelActionTips.SetToolTip(stop, "Stop this Hub-owned MCP service. The editor and project remain open.");
            services.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            services.Controls.Add(row, 0, services.Controls.Count);
        }
        flow.Controls.Add(services); flow.Controls.SetChildIndex(services, 1);

        var project = new TableLayoutPanel { Name = "GodotProjectSelector", ColumnCount = 1, RowCount = 4,
            Height = S(155), Padding = new Padding(S(10)), BackColor = Color.FromArgb(18, 26, 34), Margin = new Padding(0, 0, 0, S(10)) };
        project.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 25, 34, 37, 24 }) project.RowStyles.Add(new RowStyle(SizeType.Absolute, S(height)));
        project.Controls.Add(new Label { Text = "GODOT PROJECT", Dock = DockStyle.Fill, ForeColor = Warn, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        _projectPicker = new ComboBox { Name = "GodotProjectPicker", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9F), IntegralHeight = false, DropDownHeight = S(200) };
        _projectPicker.DropDownWidth = S(650);
        project.Controls.Add(_projectPicker, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var select = MakeButton("Switch", async (_, _) => await SwitchProjectAsync()); select.Name = "SwitchGodotProject";
        var add = MakeButton("Add project…", (_, _) => RegisterProject()); add.Name = "AddGodotProject";
        foreach (var button in new[] { select, add }) { button.Width = S(105); button.Height = S(30); button.Font = new Font("Segoe UI", 9F); actions.Controls.Add(button); }
        project.Controls.Add(actions, 0, 2);
        project.Controls.Add(new Label { Text = "Register a folder with project.godot", Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font("Segoe UI", 8F) }, 0, 3);
        flow.Controls.Add(project); flow.Controls.SetChildIndex(project, 2);
        // The project selector replaces the static current-project rail card.
        var oldProject = flow.Controls.Cast<Control>().FirstOrDefault(c => HelperDescendants<Label>(c).Any(l => l.Text == "CURRENT PROJECT"));
        if (oldProject is not null) { flow.Controls.Remove(oldProject); oldProject.Dispose(); }
        void FitRail() { foreach (var control in new[] { services, project }) control.Width = Math.Max(S(180), flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4); }
        flow.SizeChanged += (_, _) => FitRail(); FitRail(); ReloadProjects();

        var navigation = shell.GetControlFromPosition(0, 0)!;
        var nav = HelperDescendants<TableLayoutPanel>(navigation).First(p => p.RowCount >= 8 && p.ColumnCount == 1);
        var files = MakeButton("Files / Файлы", async (_, _) => await OpenFileInboxAsync());
        files.Name = "OpenFileInbox"; files.Dock = DockStyle.Fill; files.Height = S(42); files.Font = new Font("Segoe UI", 10F);
        nav.Controls.Add(files, 0, 4); nav.RowStyles[4].Height = S(48);

        _logTimer.Tick += (_, _) =>
        {
            for (var i = 0; i < 100 && _pendingLogs.TryDequeue(out var message); i++)
            { Interlocked.Decrement(ref _pendingLogCount); Log(message); }
            FlushLogView();
        };
        _logTimer.Start();
        FormClosed += (_, _) => { _logTimer.Stop(); _logTimer.Dispose(); };
        AttachAllowlistRecovery();
    }

    private void ReloadProjects(string? selectedRoot = null)
    {
        if (_projectPicker is null) return;
        var root = selectedRoot ?? _godot.Settings.ProjectRoot;
        _projectPicker.Items.Clear();
        foreach (var entry in GodotProjectRegistry.Load()) _projectPicker.Items.Add(entry);
        _projectPicker.SelectedItem = _projectPicker.Items.Cast<GodotProjectEntry>().FirstOrDefault(p => string.Equals(p.ProjectRoot, root, StringComparison.OrdinalIgnoreCase));
        _tunnelActionTips.SetToolTip(_projectPicker, root);
    }

    private void RegisterProject()
    {
        using var picker = new OpenFileDialog { Title = "Register Godot project", Filter = "Godot project|project.godot", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { var entry = GodotProjectRegistry.Register(picker.FileName); ReloadProjects(entry.ProjectRoot); Log("Registered project: " + entry.ProjectRoot); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Register project", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task SwitchProjectAsync()
    {
        if (_changingProject || _projectPicker?.SelectedItem is not GodotProjectEntry entry) return;
        _changingProject = true; _projectPicker.Enabled = false;
        await RunAdapterActionAsync("Switch Godot project", async ct =>
        {
            await SelectGodotProjectAsync(entry.ProjectRoot, ct);
        });
        _projectPicker.Enabled = true; _changingProject = false; ReloadProjects();
        await RefreshStatusAsync();
    }

    internal async Task SelectGodotProjectAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        await _godot.SelectProjectAsync(projectRoot, cancellationToken);
        Log("Active Godot project: " + projectRoot);
        try
        {
            await ApplyFileAccessAsync(FileInbox.EnsureAgentAccess(projectRoot));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log("Project switched. Local Files access needs attention: " + ex.Message);
        }
    }

    private async Task ApplyFileAccessAsync(bool changed)
    {
        if (!changed) return;
        var status = await _localFiles.GetStatusAsync();
        if (status.State == AdapterState.Running)
        {
            if (_localFiles.SettingsSummary.ConfigValid) await _localFiles.RestartAsync();
            else
            {
                await _localFiles.StopAsync();
                Log("Local Files stopped. Resolve unavailable allowlist folders or add an allowed folder before starting it again.");
            }
        }
        else if (status.State == AdapterState.External)
            Log("File access saved. Restart the external Local Files service to load the new folder.");
    }

    private async Task StopBridgeAsync(string id)
    {
        var adapter = id switch { "local-files" => (IHubAdapter)_localFiles, "blender" => _blender, "godot" => _godot, _ => _helper };
        if (_bridgeRows.TryGetValue(id, out var row)) row.Stop.Enabled = false;
        await RunAdapterActionAsync("Stop " + adapter.DisplayName, adapter.StopAsync);
        await RefreshStatusAsync();
    }

    private void SetBridgeRow(string id, AdapterState state, string detail)
    {
        if (!_bridgeRows.TryGetValue(id, out var row)) return;
        var label = state switch { AdapterState.Running => "Ready", AdapterState.External => "External", AdapterState.Stopped => "Stopped", AdapterState.Starting => "Starting", AdapterState.NotConfigured => "Not configured", _ => "Needs attention" };
        row.Status.Text = label + (detail.Length > 0 ? " · " + detail : "");
        row.Status.ForeColor = state is AdapterState.Running or AdapterState.External ? Good : state is AdapterState.Stopped or AdapterState.NotConfigured ? Muted : Warn;
        row.Stop.Enabled = state == AdapterState.Running;
        _tunnelActionTips.SetToolTip(row.Status, row.Status.Text);
    }

    private void UpdateBridgeRail(AdapterStatus local, BlenderAdapterSnapshot blender, GodotAdapterSnapshot godot)
    {
        SetBridgeRow("local-files", local.State, local.Summary);
        SetBridgeRow("blender", blender.TunnelStatus.State, blender.BridgeConnected ? "bridge online" : "bridge offline");
        SetBridgeRow("godot", godot.TunnelStatus.State, godot.Configured
            ? $"editor {(godot.EditorBridgeConnected ? "online" : "offline")} / runtime {(godot.RuntimeBridgeConnected ? "online" : "idle")}"
            : godot.Plugin.Message);
        if (!godot.EditorBridgeConnected && _bridgeRows.TryGetValue("godot", out var godotRow))
        {
            godotRow.Status.ForeColor = Warn;
            _tunnelActionTips.SetToolTip(godotRow.Status, godot.EditorBridgeIssue ?? "Editor bridge is offline.");
        }
        if (_latestHelper is not null) SetBridgeRow("mcp-helper", _latestHelper.State, _latestHelper.Summary);
    }

    private async Task OpenFileInboxAsync()
    {
        try
        {
            await ApplyFileAccessAsync(FileInbox.EnsureAgentAccess(FileInbox.Root));
            var service = await _localFiles.GetStatusAsync();
            if (service.State == AdapterState.Stopped)
            {
                await RunAdapterActionQuietAsync("Connect file inbox", _localFiles.StartAsync);
                service = await _localFiles.GetStatusAsync();
            }
            var access = service.State == AdapterState.Running
                ? "Local Files MCP подключён. Папка доступна агенту через Local Files."
                : service.State == AdapterState.External
                ? "Внешний Local Files MCP: перезапустите его у владельца, чтобы загрузить добавленные папки."
                : "Local Files MCP не подключён. Файлы сохраняются; запустите Local Files в Hub для доступа агента.";
            using var inbox = new FileInboxForm(access, service.State == AdapterState.Running); inbox.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "File inbox", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
