using ConstantineHub.Adapters.Blender;
using ConstantineHub.Adapters.Godot;
using ConstantineHub.Adapters.LocalFiles;
using ConstantineHub.Core;

namespace ConstantineHub;

internal sealed partial class MainForm : Form
{
    private readonly LocalFilesAdapter _localFiles = new();
    private readonly BlenderAdapter _blender = new();
    private readonly GodotAdapter _godot = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 5000 };
    private readonly RichTextBox _log = new();
    private readonly StableTrayIcon _trayIcon = new();
    private readonly UpdateService _updates = new();
    private readonly List<string> _allLogLines = new();
    private Button? _verboseButton;
    private bool _verboseLogs;
    private IReadOnlyList<ServiceConnection> _connections = [];
    private readonly Dictionary<string, string> _actionErrors = new();
    private readonly Dictionary<string, (Button Stop, Button Restart, Label Status)> _tunnelActions = new();
    private readonly ToolTip _tunnelActionTips = new() { ShowAlways = true };
    internal ConnectionOverview Connections => ConnectionOverview.Build(_connections, _actionErrors);

    private readonly Label _localTunnel = new();
    private readonly Label _localRoots = new();
    private readonly Label _localAccess = new();
    private readonly Label _blenderApp = new();
    private readonly Label _blenderBridge = new();
    private readonly Label _blenderTunnel = new();
    private readonly Label _blenderSkills = new();
    private readonly Label _godotProject = new();
    private readonly Label _godotPlugin = new();
    private readonly Label _godotEditor = new();
    private readonly Label _godotRuntime = new();
    private readonly Label _godotTunnel = new();
    private readonly Label _godotSkills = new();

    private bool _refreshing;
    private bool _checkingUpdates;
    private bool _trayHintShown;
    private bool _exitRequested;
    private readonly bool _enableTray;
    internal string UpdateLogTail => string.Join(Environment.NewLine, _allLogLines.TakeLast(40));
    private FormWindowState _restoreWindowState = FormWindowState.Normal;

    private static readonly Color Good = Color.FromArgb(92, 201, 120);
    private static readonly Color Bad = Color.FromArgb(232, 104, 104);
    private static readonly Color Warn = Color.FromArgb(232, 188, 104);
    private static readonly Color Muted = Color.FromArgb(170, 174, 184);

    internal MainForm(bool enableTray = false)
    {
        _enableTray = enableTray;
        Text = "ConHub.";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 980);
        Size = new Size(1180, 1120);
        BackColor = Color.FromArgb(22, 24, 29);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 13.5F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = AppIconProvider.Current;

        InitializeReconnect();
        BuildUi();
        ConfigureTray();

        _localFiles.LogLine += LogFromAnyThread;
        _blender.LogLine += LogFromAnyThread;
        _godot.LogLine += LogFromAnyThread;
        _nvidia.LogLine += LogFromAnyThread;
        _helper.LogLine += LogFromAnyThread;
        _statusTimer.Tick += async (_, _) =>
        {
            if (_reconnect is not null) _ = _reconnect.CheckAsync();
            await RefreshStatusAsync();
        };
        _statusTimer.Start();

        Resize += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized)
                _restoreWindowState = WindowState;
        };
        VisibleChanged += (_, _) =>
        {
            _statusTimer.Interval = Visible ? 5000 : 15000;
            FlushLogView();
        };
        _log.VisibleChanged += (_, _) => FlushLogView();
        Disposed += (_, _) => { _trayIcon.Dispose(); _reconnect?.Dispose(); };
        FormClosing += (_, e) =>
        {
            if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
            }
        };

        Shown += async (_, _) =>
        {
            Log($"ConHub. v{GetDisplayVersion()} started.");
            Log("Local Files MCP, Blender MCP and Godot MCP are managed adapters.");
            await RefreshStatusAsync();
            if (_enableTray) _ = CheckForUpdatesAsync(silentWhenCurrent: true);
        };

        FormClosed += (_, _) =>
        {
            _statusTimer.Stop();
            _reconnect?.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _statusTimer.Dispose();
            _tunnelActionTips.Dispose();
            _localFiles.Dispose();
            _blender.Dispose();
            _godot.Dispose();
            _nvidia.Dispose();
            _helper.LogLine -= LogFromAnyThread;
            _helper.Dispose();
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 6
        };
        var statusRowHeight = GetStatusRowHeight();
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, GetCardRowHeight(3, statusRowHeight)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, GetCardRowHeight(4, statusRowHeight)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, GetCardRowHeight(6, statusRowHeight)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label
        {
            Text = "ConHub.",
            Font = new Font("Segoe UI Semibold", 24F),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = $"Local MCP control plane  •  v{GetDisplayVersion()}",
            Font = new Font("Segoe UI", 11.5F),
            ForeColor = Muted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true,
            Padding = new Padding(3, 0, 0, 0)
        }, 0, 1);
        root.Controls.Add(header, 0, 0);

        root.Controls.Add(BuildLocalFilesCard(), 0, 1);
        root.Controls.Add(BuildBlenderCard(), 0, 2);
        root.Controls.Add(BuildGodotCard(), 0, 3);

        var globalActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 4)
        };
        globalActions.Controls.Add(MakeButton("Start All", async (_, _) => await StartAllAsync(), primary: true));
        globalActions.Controls.Add(MakeButton("Stop Managed", async (_, _) => await StopAllOwnedAsync()));
        globalActions.Controls.Add(MakeButton("Refresh", async (_, _) => await RefreshStatusAsync()));
        globalActions.Controls.Add(MakeButton("Check Updates", async (_, _) => await CheckForUpdatesAsync()));
        _verboseButton = MakeButton("Verbose: Off", (_, _) => ToggleVerboseLogs());
        globalActions.Controls.Add(_verboseButton);
        globalActions.Controls.Add(MakeButton("Save Log", (_, _) => SaveLog()));
        root.Controls.Add(globalActions, 0, 4);

        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.DetectUrls = false;
        _log.BackColor = Color.FromArgb(15, 17, 21);
        _log.ForeColor = Color.Gainsboro;
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.Font = new Font("Cascadia Mono", 9.5F);
        _log.WordWrap = false;
        root.Controls.Add(_log, 0, 5);
    }

    private Control BuildLocalFilesCard()
    {
        var card = BuildCard("LOCAL FILES MCP", 3, out var content, out var actions);
        AddStatusRow(content, 0, "Tunnel", _localTunnel);
        AddStatusRow(content, 1, "Allowed roots", _localRoots);
        AddStatusRow(content, 2, "Access", _localAccess);

        actions.Controls.Add(MakeButton("Start", async (_, _) => await RunAdapterActionAsync("Start Local Files", _localFiles.StartAsync), primary: true));
        AddTunnelActions(actions, _localFiles, _localTunnel, "Local Files");
        actions.Controls.Add(MakeButton("Doctor", async (_, _) => await DoctorLocalFilesAsync()));
        actions.Controls.Add(MakeButton("Install Plugin", async (_, _) => await InstallTunnelPluginAsync()));
        actions.Controls.Add(MakeButton("Settings", async (_, _) => await OpenLocalFilesSettingsAsync()));
        return card;
    }

    private Control BuildBlenderCard()
    {
        var card = BuildCard("BLENDER MCP", 4, out var content, out var actions);
        AddStatusRow(content, 0, "Blender", _blenderApp);
        AddStatusRow(content, 1, "Bridge", _blenderBridge);
        AddStatusRow(content, 2, "Tunnel", _blenderTunnel);
        AddStatusRow(content, 3, "Skills", _blenderSkills);

        actions.Controls.Add(MakeButton("Start", async (_, _) => await RunAdapterActionAsync("Start Blender MCP", _blender.StartAsync), primary: true));
        AddTunnelActions(actions, _blender, _blenderTunnel, "Blender MCP");
        actions.Controls.Add(MakeButton("Doctor", async (_, _) => await DoctorBlenderAsync()));
        return card;
    }

    private Control BuildGodotCard()
    {
        var card = BuildCard("GODOT MCP", 6, out var content, out var actions);
        AddStatusRow(content, 0, "Project", _godotProject);
        AddStatusRow(content, 1, "Plugin", _godotPlugin);
        AddStatusRow(content, 2, "Editor bridge", _godotEditor);
        AddStatusRow(content, 3, "Runtime bridge", _godotRuntime);
        AddStatusRow(content, 4, "Tunnel", _godotTunnel);
        AddStatusRow(content, 5, "Skills/contracts", _godotSkills);

        actions.Controls.Add(MakeButton("Start", async (_, _) => await RunAdapterActionAsync("Start Godot MCP", _godot.StartAsync), primary: true));
        AddTunnelActions(actions, _godot, _godotTunnel, "Godot MCP");
        actions.Controls.Add(MakeButton("Install Plugin", (_, _) => InstallGodotPlugin()));
        actions.Controls.Add(MakeButton("Doctor", async (_, _) => await DoctorGodotAsync()));
        return card;
    }

    private void AddTunnelActions(FlowLayoutPanel actions, IHubAdapter adapter, Label status, string name)
    {
        var stop = MakeButton("Stop", async (_, _) => await RunAdapterActionAsync("Stop " + name, adapter.StopAsync));
        var restart = MakeButton("Restart", async (_, _) => await RunAdapterActionAsync("Restart " + name, adapter.RestartAsync));
        actions.Controls.Add(stop);
        actions.Controls.Add(restart);
        _tunnelActions.Add(adapter.Id, (stop, restart, status));
    }

    internal void UpdateTunnelActions(string id, AdapterState state)
    {
        var controls = _tunnelActions[id];
        var external = state == AdapterState.External;
        controls.Stop.Enabled = controls.Restart.Enabled = !external;
        controls.Status.Tag = external ? ExternalTunnelControlException.Guidance : null;
        _tunnelActionTips.SetToolTip(controls.Status, external ? ExternalTunnelControlException.Guidance : null);
    }

    private Panel BuildCard(
        string title,
        int statusRows,
        out TableLayoutPanel content,
        out FlowLayoutPanel actions)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 33, 40),
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        card.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 13.5F),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);

        content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = statusRows,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var statusRowHeight = GetStatusRowHeight();
        for (var i = 0; i < statusRows; i++)
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, statusRowHeight));
        layout.Controls.Add(content, 0, 1);

        actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 4, 0, 0)
        };
        layout.Controls.Add(actions, 0, 2);
        return card;
    }

    private int GetStatusRowHeight()
    {
        var measured = TextRenderer.MeasureText("Ag", Font);
        return Math.Max(34, measured.Height + 6);
    }

    private static int GetCardRowHeight(int statusRows, int statusRowHeight)
        => 118 + (statusRows * statusRowHeight);

    private static void AddStatusRow(TableLayoutPanel table, int row, string name, Label value)
    {
        table.Controls.Add(new Label
        {
            Text = name,
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.Silver,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(3, 2, 12, 2)
        }, 0, row);

        value.Dock = DockStyle.Fill;
        value.Text = "● Checking…";
        value.ForeColor = Muted;
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.AutoEllipsis = true;
        value.Margin = new Padding(3, 2, 6, 2);
        table.Controls.Add(value, 1, row);
    }

    private static Button MakeButton(string text, EventHandler handler, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Width = text.Length > 10 ? 174 : 130,
            Height = 40,
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(55, 106, 175) : Color.FromArgb(42, 46, 55),
            ForeColor = Color.White
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 74, 85);
        button.Tag = handler;
        button.Click += handler;
        return button;
    }

    // Shell actions must work even when their legacy button is in a collapsed card.
    internal static void InvokeButtonAction(Button? button)
    {
        if (button is { IsDisposed: false, Enabled: true, Tag: EventHandler action })
            action(button, EventArgs.Empty);
    }

    private void ConfigureTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open ConHub.", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Tray history…", null, (_, _) => { using var history = new TrayHistoryForm(); history.ShowDialog(this); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start All", null, async (_, _) => await StartAllAsync());
        menu.Items.Add("Stop Hub-Owned", null, async (_, _) => await StopAllOwnedAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => RequestExit());

        _trayIcon.Text = "ConHub.";
        _trayIcon.Icon = AppIconProvider.Current;
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = _enableTray;
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    internal void RequestExit()
    {
        _exitRequested = true;
        Close();
    }

    private void HideToTray()
    {
        if (_enableTray && !_trayIcon.IsRegistered)
        {
            Log("Tray icon unavailable: " + _trayIcon.LastError);
            WindowState = FormWindowState.Normal;
            return;
        }
        if (WindowState != FormWindowState.Minimized)
            _restoreWindowState = WindowState;

        Hide();
        ShowInTaskbar = false;

        if (_trayHintShown || !_enableTray)
            return;

        _trayHintShown = true;
        try
        {
            _trayIcon.ShowBalloonTip(
                1500,
                "ConHub.",
                "Hub is still running. Double-click the tray icon to restore it.",
                ToolTipIcon.Info);
        }
        catch
        {
            // Notification support varies by Windows settings; tray behavior still works.
        }
    }

    internal void RestoreFromTray()
    {
        if (!Visible)
            Show();

        ShowInTaskbar = true;
        WindowState = _restoreWindowState == FormWindowState.Minimized
            ? FormWindowState.Normal
            : _restoreWindowState;
        Activate();
        BringToFront();
    }

    private async Task RefreshStatusAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            var local = await _localFiles.GetStatusAsync();
            SetState(_localTunnel, local.State, local.Summary);
            UpdateTunnelActions(_localFiles.Id, local.State);

            var summary = _localFiles.SettingsSummary;
            RefreshAllowlistNotification();
            SetStatus(_localRoots, summary.ConfigValid, summary.ConfigValid
                ? $"{summary.RootCount} configured"
                : summary.Message);
            SetStatus(_localAccess, summary.ConfigValid,
                summary.ConfigValid
                    ? $"Read • Write {(summary.AnyWrite ? "ON" : "off")} • Delete {(summary.AnyDelete ? "ON" : "off")}"
                    : "Config invalid");

            var blender = await _blender.GetSnapshotAsync();
            SetOptionalStatus(_blenderApp, blender.BlenderRunning,
                blender.BlenderRunning ? "Running" : "Not running");
            SetOptionalStatus(_blenderBridge, blender.BridgeConnected,
                blender.BridgeConnected
                    ? $"Connected {blender.BridgeEndpoint}"
                    : $"Not listening on {blender.BridgeEndpoint}");
            SetState(_blenderTunnel, blender.TunnelStatus.State, blender.TunnelStatus.Summary);
            UpdateTunnelActions(_blender.Id, blender.TunnelStatus.State);
            SetStatus(_blenderSkills, blender.Skills.Success,
                blender.Skills.Success
                    ? $"{blender.Skills.VerifiedCount}/{blender.Skills.SourceCount} verified"
                    : blender.Skills.Message);

            var godot = await _godot.GetSnapshotAsync();
            SetStatus(_godotProject, godot.Configured, godot.Configured ? godot.ProjectRoot : godot.Plugin.Message);
            SetStatus(_godotPlugin, godot.Plugin.Installed && godot.Plugin.Enabled, godot.Plugin.Message);
            SetOptionalStatus(_godotEditor, godot.EditorBridgeConnected,
                godot.EditorBridgeConnected
                    ? $"Connected {_godot.Settings.EditorHost}:{_godot.Settings.EditorPort}"
                    : godot.EditorBridgeIssue ?? $"Not listening on {_godot.Settings.EditorHost}:{_godot.Settings.EditorPort}");
            SetOptionalStatus(_godotRuntime, godot.RuntimeBridgeConnected,
                godot.RuntimeBridgeConnected
                    ? $"Connected {_godot.Settings.RuntimeHost}:{_godot.Settings.RuntimePort}"
                    : "Idle / runtime not running");
            SetState(_godotTunnel, godot.TunnelStatus.State, godot.TunnelStatus.Summary);
            UpdateTunnelActions(_godot.Id, godot.TunnelStatus.State);
            await RefreshNvidiaAsync();
            await RefreshHelperAsync();
            SetStatus(_godotSkills, godot.SkillsOk, godot.SkillsSummary);
            _connections = [
                new("Local Files", local.State, SetupIssue: summary.ConfigValid ? null : summary.Message),
                new("Blender", blender.TunnelStatus.State, blender.BridgeConnected,
                    blender.Skills.Success ? null : blender.Skills.Message),
                new("NVIDIA App", _latestNvidia?.TunnelStatus.State ?? AdapterState.NotConfigured, _latestNvidia?.HttpAvailable),
                new("Godot editor", godot.TunnelStatus.State, godot.EditorBridgeConnected,
                    !godot.Configured ? godot.Plugin.Message :
                    !godot.Plugin.Installed || !godot.Plugin.Enabled ? godot.Plugin.Message :
                    !godot.SkillsOk ? godot.SkillsSummary : null)
            ];
            UpdateBridgeRail(local, blender, godot);
            _actionErrors.Remove("Status check");
        }
        catch (Exception ex)
        {
            Log("Status refresh failed: " + ex.Message);
            _actionErrors["Status check"] = ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task StartAllAsync()
    {
        Log("Start All requested.");
        await RunAdapterActionQuietAsync("Start Local Files", _localFiles.StartAsync);
        await RunAdapterActionQuietAsync("Start Blender MCP", _blender.StartAsync);
        await RunAdapterActionQuietAsync("Start Godot MCP", _godot.StartAsync);
        await StartNvidiaIfConfiguredAsync();
        if (HelperInGlobalActions()) await RunAdapterActionQuietAsync("Start MCP Helper", _helper.StartAsync);
        await RefreshStatusAsync();
    }

    private async Task StopAllOwnedAsync()
    {
        Log("Stop Hub-Owned requested. External/adopted tunnels will be left untouched.");
        if (HelperInGlobalActions()) await RunAdapterActionQuietAsync("Stop MCP Helper", _helper.StopAsync);
        await RunAdapterActionQuietAsync("Stop Godot MCP", _godot.StopAsync);
        await RunAdapterActionQuietAsync("Stop NVIDIA App MCP", _nvidia.StopAsync);
        await RunAdapterActionQuietAsync("Stop Blender MCP", _blender.StopAsync);
        await RunAdapterActionQuietAsync("Stop Local Files", _localFiles.StopAsync);
        await RefreshStatusAsync();
    }

    internal async Task RunAdapterActionQuietAsync(
        string title,
        Func<CancellationToken, Task> action)
    {
        try
        {
            Log(title + " requested.");
            await action(CancellationToken.None);
            _actionErrors.Remove(ActionKey(title, action));
        }
        catch (ExternalTunnelControlException ex)
        {
            Log(title + " unavailable: " + ex.Message);
            _actionErrors.Remove(ActionKey(title, action));
        }
        catch (Exception ex)
        {
            Log(title + " FAILED: " + ex.Message);
            _actionErrors[ActionKey(title, action)] = ex.Message;
        }
    }

    private async Task RunAdapterActionAsync(
        string title,
        Func<CancellationToken, Task> action)
    {
        try
        {
            Log(title + " requested.");
            await action(CancellationToken.None);
            _actionErrors.Remove(ActionKey(title, action));
        }
        catch (ExternalTunnelControlException ex)
        {
            Log(title + " unavailable: " + ex.Message);
            _actionErrors.Remove(ActionKey(title, action));
            MessageBox.Show(this, ex.Message, "Tunnel managed outside Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log(title + " FAILED: " + ex.Message);
            _actionErrors[ActionKey(title, action)] = ex.Message;
            ErrorReporter.Show(ex, title, this);
        }
        finally
        {
            await RefreshStatusAsync();
        }
    }

    private static string ActionKey(string title, Func<CancellationToken, Task> action)
        => action.Target is IHubAdapter adapter ? adapter.DisplayName : title;

    internal void OpenCanonicalFolder(bool skills)
    {
        var name = skills ? "skills directory" : "contracts source";
        try
        {
            var folder = CanonicalKnowledge.ResolveFolder(skills, _blender.Settings.TunnelProfile,
                GodotSettingsStore.LoadToolsCRoot());
            CanonicalKnowledge.OpenExplorer(folder);
            Log($"Opened canonical Tools_C {name}: {folder}");
        }
        catch (Exception ex)
        {
            Log($"Cannot open canonical Tools_C {name}: {ex}");
            MessageBox.Show(this, $"Canonical Tools_C {name} not found.\nSee Diagnostics for details.",
                "Canonical Tools_C", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task DoctorLocalFilesAsync()
    {
        try
        {
            Log("Local Files Doctor requested.");
            var result = await _localFiles.DoctorAsync();
            Log(result);
        }
        catch (Exception ex)
        {
            Log("Local Files Doctor FAILED: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Local Files Doctor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            await RefreshStatusAsync();
        }
    }

    private async Task DoctorBlenderAsync()
    {
        try
        {
            Log("Blender Doctor requested.");
            var result = await _blender.DoctorAsync();
            Log(result);
        }
        catch (Exception ex)
        {
            Log("Blender Doctor FAILED: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Blender Doctor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            await RefreshStatusAsync();
        }
    }

    private async Task DoctorGodotAsync()
    {
        try
        {
            Log("Godot Doctor requested.");
            var result = await _godot.DoctorAsync();
            Log(result);
        }
        catch (Exception ex)
        {
            Log("Godot Doctor FAILED: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Godot Doctor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            await RefreshStatusAsync();
        }
    }

    private void InstallGodotPlugin()
    {
        try
        {
            var status = _godot.InstallOrUpdatePlugin();
            Log("Godot plugin: " + status.Message);
            MessageBox.Show(
                this,
                status.Message + Environment.NewLine + Environment.NewLine +
                "If the Godot editor is already open, reload or restart the project so the editor plugin can activate.",
                "Godot plugin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("Godot plugin install FAILED: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Godot plugin", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        _ = RefreshStatusAsync();
    }

    private async Task InstallTunnelPluginAsync()
    {
        await RunAdapterActionAsync("Install Tunnel MCP plugin", async cancellationToken =>
        {
            var executable = ExecutableLocator.FindTunnelClient()
                ?? throw new FileNotFoundException("tunnel-client.exe was not found.");
            await TunnelPluginInstaller.InstallAsync(executable, log: Log, cancellationToken: cancellationToken);
            MessageBox.Show(this, "Tunnel MCP plugin installed. Restart Codex to load it.",
                "Tunnel MCP plugin", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private async Task OpenLocalFilesSettingsAsync()
    {
        using var form = new LocalFilesSettingsForm();
        if (form.ShowDialog(this) != DialogResult.OK)
            return;

        Log("Local Files allowlist saved.");
        await RunAdapterActionAsync("Apply Local Files allowlist", _ => ApplyFileAccessAsync(true));

        await RefreshStatusAsync();
    }

    private async Task CheckForUpdatesAsync(bool silentWhenCurrent = false)
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        try
        {
            var current = typeof(MainForm).Assembly.GetName().Version ?? new Version(0, 1, 0);
            Log("Checking GitHub Releases for ConHub. updates…");
            var update = await _updates.CheckForStableUpdateAsync(current);
            if (update is null)
            {
                Log("No newer stable release is available.");
                if (!silentWhenCurrent)
                    MessageBox.Show(this, "You already have the latest stable ConHub. release.", "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Log($"Update available: {GetDisplayVersion()} → {update.Tag}");
            var answer = MessageBox.Show(
                this,
                $"A new ConHub. release is available.\n\nCurrent: {GetDisplayVersion()}\nAvailable: {update.Tag}\n\nDownload, verify SHA-256 and install it now?",
                "Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (answer != DialogResult.Yes)
                return;

            Log("Downloading and verifying update package…");
            await _updates.StageAndLaunchAsync(update, current);
            Log("Updater started. ConHub. will now exit and restart.");
            _trayIcon.Visible = false;
            _exitRequested = true;
            Application.Exit();
        }
        catch (Exception ex)
        {
            Log("Update FAILED: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _checkingUpdates = false; }
    }

    private void ToggleVerboseLogs()
    {
        _verboseLogs = !_verboseLogs;
        if (_verboseButton is not null)
            _verboseButton.Text = _verboseLogs ? "Verbose: On" : "Verbose: Off";

        _logDirty = true;
        FlushLogView();
    }

    private static bool IsVerboseTunnelLine(string line)
    {
        if (line.Contains("MCP Helper: info:", StringComparison.Ordinal) ||
            line.Contains("MCP Helper: dbug:", StringComparison.Ordinal) ||
            line.Contains("MCP Helper: trce:", StringComparison.Ordinal)) return true;
        if (!line.Contains("{\"time\":", StringComparison.Ordinal))
            return false;
        return !line.Contains("\"level\":\"ERROR\"", StringComparison.OrdinalIgnoreCase) &&
               !line.Contains("FAILED", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetStatus(Label label, bool ok, string text)
    {
        label.Text = "● " + text;
        label.ForeColor = ok ? Good : Bad;
    }

    private static void SetOptionalStatus(Label label, bool active, string text)
    {
        label.Text = "● " + text;
        label.ForeColor = active ? Good : Warn;
    }

    private static void SetState(Label label, AdapterState state, string text)
    {
        label.Text = "● " + text;
        label.ForeColor = state switch
        {
            AdapterState.Running or AdapterState.External => Good,
            AdapterState.Starting or AdapterState.Degraded => Warn,
            AdapterState.Stopped or AdapterState.NotConfigured => Muted,
            _ => Bad
        };
    }

    private void Log(string message)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            QueueLog(message);
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _allLogLines.Add(line);
        if (_allLogLines.Count > 3000)
        {
            _allLogLines.RemoveRange(0, 500);
            _logDirty = true;
        }
        if (_verboseLogs || !IsVerboseTunnelLine(line)) _logDirty = true;
    }

    private void LogFromAnyThread(string message) => Log(message);

    private void SaveLog()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            DefaultExt = "md",
            FileName = $"Constantine-Hub-log-{DateTime.Now:yyyy-MM-dd_HHmmss}.md"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        File.WriteAllText(dialog.FileName, string.Join(Environment.NewLine, _allLogLines) + Environment.NewLine);
        Log("Log saved (recent session diagnostics): " + dialog.FileName);
    }

    private static string GetDisplayVersion()
    {
        var version = typeof(MainForm).Assembly.GetName().Version;
        return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
