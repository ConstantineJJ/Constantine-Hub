using ConstantineHub.Adapters.Blender;
using ConstantineHub.Adapters.Godot;
using ConstantineHub.Adapters.LocalFiles;
using ConstantineHub.Core;

namespace ConstantineHub;

internal sealed class MainForm : Form
{
    private readonly LocalFilesAdapter _localFiles = null!;
    private readonly BlenderAdapter _blender = null!;
    private readonly GodotAdapter _godot = null!;
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1500 };
    private readonly RichTextBox _log = new();
    private readonly NotifyIcon _trayIcon = new();

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
    private bool _trayHintShown;
    private FormWindowState _restoreWindowState = FormWindowState.Normal;

    private static readonly Color Good = Color.FromArgb(92, 201, 120);
    private static readonly Color Bad = Color.FromArgb(232, 104, 104);
    private static readonly Color Warn = Color.FromArgb(232, 188, 104);
    private static readonly Color Muted = Color.FromArgb(170, 174, 184);

    internal MainForm(bool layoutPreview = false)
    {
        SuspendLayout();
        Text = "Constantine Hub";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(900, 720);
        Size = new Size(1280, 1200);
        BackColor = Color.FromArgb(22, 24, 29);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 15F);
        Icon = AppIconProvider.Current;

        BuildUi();
        ResumeLayout(performLayout: true);
        // The layout harness renders the production controls without settings, probes or processes.
        if (layoutPreview)
            return;
        _localFiles = new LocalFilesAdapter();
        _blender = new BlenderAdapter();
        _godot = new GodotAdapter();
        ConfigureTray();

        _localFiles.LogLine += LogFromAnyThread;
        _blender.LogLine += LogFromAnyThread;
        _godot.LogLine += LogFromAnyThread;
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _statusTimer.Start();

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                HideToTray();
            else if (WindowState != FormWindowState.Minimized)
                _restoreWindowState = WindowState;
        };

        Shown += async (_, _) =>
        {
            Log($"Constantine Hub v{GetDisplayVersion()} started.");
            Log("Local Files MCP, Blender MCP and Godot MCP are managed adapters.");
            await RefreshStatusAsync();
        };

        FormClosed += (_, _) =>
        {
            _statusTimer.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _localFiles.Dispose();
            _blender.Dispose();
            _godot.Dispose();
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // Keep the compact log viewport independent of card/font growth (420 px at 150%).
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 280));
        Controls.Add(root);

        var viewport = new CardViewport { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < cards.RowCount; i++)
            cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        viewport.Controls.Add(cards);
        // Keep the scroll extent tied to the complete content after font/window changes.
        cards.SizeChanged += (_, _) => viewport.AutoScrollMinSize = new Size(0, cards.Height);
        root.Controls.Add(viewport, 0, 0);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 10)
        };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(new Label
        {
            Text = "Constantine Hub",
            Font = new Font("Segoe UI Semibold", 28F),
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = $"Local MCP control plane  •  v{GetDisplayVersion()}",
            Font = new Font("Segoe UI", 13F),
            AutoSize = true,
            ForeColor = Muted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true,
            Padding = new Padding(3, 0, 0, 0)
        }, 0, 1);
        cards.Controls.Add(header, 0, 0);

        cards.Controls.Add(BuildLocalFilesCard(), 0, 1);
        cards.Controls.Add(BuildBlenderCard(), 0, 2);
        cards.Controls.Add(BuildGodotCard(), 0, 3);

        var globalActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 4)
        };
        globalActions.Controls.Add(MakeButton("Start All", async (_, _) => await StartAllAsync(), primary: true));
        globalActions.Controls.Add(MakeButton("Stop Managed", async (_, _) => await StopAllOwnedAsync()));
        globalActions.Controls.Add(MakeButton("Refresh", async (_, _) => await RefreshStatusAsync()));
        globalActions.Controls.Add(MakeButton("Save Log", (_, _) => SaveLog()));
        root.Controls.Add(globalActions, 0, 1);

        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BackColor = Color.FromArgb(15, 17, 21);
        _log.ForeColor = Color.Gainsboro;
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.Font = new Font("Cascadia Mono", 9.5F);
        _log.WordWrap = false;
        root.Controls.Add(_log, 0, 2);
    }

    private Control BuildLocalFilesCard()
    {
        var card = BuildCard("LOCAL FILES MCP", 3, out var content, out var actions);
        AddStatusRow(content, 0, "Tunnel", _localTunnel);
        AddStatusRow(content, 1, "Allowed roots", _localRoots);
        AddStatusRow(content, 2, "Access", _localAccess);

        actions.Controls.Add(MakeButton("Start", async (_, _) => await RunAdapterActionAsync("Start Local Files", _localFiles.StartAsync), primary: true));
        actions.Controls.Add(MakeButton("Stop", async (_, _) => await RunAdapterActionAsync("Stop Local Files", _localFiles.StopAsync)));
        actions.Controls.Add(MakeButton("Restart", async (_, _) => await RunAdapterActionAsync("Restart Local Files", _localFiles.RestartAsync)));
        actions.Controls.Add(MakeButton("Doctor", async (_, _) => await DoctorLocalFilesAsync()));
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
        actions.Controls.Add(MakeButton("Stop", async (_, _) => await RunAdapterActionAsync("Stop Blender MCP", _blender.StopAsync)));
        actions.Controls.Add(MakeButton("Restart", async (_, _) => await RunAdapterActionAsync("Restart Blender MCP", _blender.RestartAsync)));
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
        actions.Controls.Add(MakeButton("Stop", async (_, _) => await RunAdapterActionAsync("Stop Godot MCP", _godot.StopAsync)));
        actions.Controls.Add(MakeButton("Restart", async (_, _) => await RunAdapterActionAsync("Restart Godot MCP", _godot.RestartAsync)));
        actions.Controls.Add(MakeButton("Install Plugin", (_, _) => InstallGodotPlugin()));
        actions.Controls.Add(MakeButton("Doctor", async (_, _) => await DoctorGodotAsync()));
        return card;
    }

    private static TableLayoutPanel BuildCard(
        string title,
        int statusRows,
        out TableLayoutPanel content,
        out FlowLayoutPanel actions)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.FromArgb(30, 33, 40),
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 10)
        };

        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < card.RowCount; i++)
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        card.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Margin = new Padding(3, 4, 3, 8),
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 15F),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);

        content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = statusRows,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < statusRows; i++)
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(content, 0, 1);

        actions = new ActionPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 0)
        };
        card.Controls.Add(actions, 0, 2);
        return card;
    }

    private sealed class ActionPanel : FlowLayoutPanel
    {
        // TableLayoutPanel can ask auto rows for a preferred size at width=1.
        // Measure wrapping at the allocated width so a single row does not reserve two rows.
        public override Size GetPreferredSize(Size proposedSize)
            => base.GetPreferredSize(new Size(proposedSize.Width <= 1 ? Width : proposedSize.Width, 0));
    }

    private sealed class CardViewport : Panel
    {
        private Control? _lastFocusedControl;

        protected override void OnLeave(EventArgs e)
        {
            _lastFocusedControl = null;
            base.OnLeave(e);
        }

        protected override Point ScrollToControl(Control activeControl)
        {
            // Status/font relayout must not undo a user's scroll by following the same
            // focused button again. A new keyboard focus still scrolls into view normally.
            if (activeControl == FindForm()?.ActiveControl)
            {
                if (activeControl == _lastFocusedControl)
                    return DisplayRectangle.Location;
                _lastFocusedControl = activeControl;
            }
            return base.ScrollToControl(activeControl);
        }
    }

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
            Margin = new Padding(3, 4, 6, 4)
        }, 0, row);

        value.AutoSize = true;
        value.Dock = DockStyle.Fill;
        value.Text = "● Checking…";
        value.ForeColor = Muted;
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.AutoEllipsis = true;
        value.Margin = new Padding(3, 4, 6, 4);
        table.Controls.Add(value, 1, row);
    }

    private static Button MakeButton(string text, EventHandler handler, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 6, 16, 6),
            Margin = new Padding(0, 0, 10, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(55, 106, 175) : Color.FromArgb(42, 46, 55),
            ForeColor = Color.White
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 74, 85);
        button.Click += handler;
        return button;
    }

    private void ConfigureTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Constantine Hub", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start All", null, async (_, _) => await StartAllAsync());
        menu.Items.Add("Stop Hub-Owned", null, async (_, _) => await StopAllOwnedAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());

        _trayIcon.Text = "Constantine Hub";
        _trayIcon.Icon = AppIconProvider.Current;
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HideToTray()
    {
        if (WindowState != FormWindowState.Minimized)
            _restoreWindowState = WindowState;

        Hide();
        ShowInTaskbar = false;

        if (_trayHintShown)
            return;

        _trayHintShown = true;
        try
        {
            _trayIcon.ShowBalloonTip(
                1500,
                "Constantine Hub",
                "Hub is still running. Double-click the tray icon to restore it.",
                ToolTipIcon.Info);
        }
        catch
        {
        }
    }

    private void RestoreFromTray()
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

            var summary = _localFiles.SettingsSummary;
            SetStatus(_localRoots, summary.ConfigValid, summary.ConfigValid
                ? $"{summary.RootCount} configured"
                : summary.Message);
            SetStatus(_localAccess, summary.ConfigValid,
                summary.ConfigValid
                    ? $"Read • Write {(summary.AnyWrite ? "ON" : "off")} • Delete {(summary.AnyDelete ? "ON" : "off")}"
                    : "Config invalid");

            var blender = await _blender.GetSnapshotAsync();
            SetStatus(_blenderApp, blender.BlenderRunning,
                blender.BlenderRunning ? "Running" : "Not running");
            SetStatus(_blenderBridge, blender.BridgeConnected,
                blender.BridgeConnected
                    ? $"Connected {blender.BridgeEndpoint}"
                    : $"Not listening on {blender.BridgeEndpoint}");
            SetState(_blenderTunnel, blender.TunnelStatus.State, blender.TunnelStatus.Summary);
            SetStatus(_blenderSkills, blender.Skills.Success,
                blender.Skills.Success
                    ? $"{blender.Skills.VerifiedCount}/{blender.Skills.SourceCount} verified"
                    : blender.Skills.Message);

            var godot = await _godot.GetSnapshotAsync();
            SetStatus(_godotProject, godot.Configured, godot.ProjectRoot);
            SetStatus(_godotPlugin, godot.Plugin.Installed && godot.Plugin.Enabled, godot.Plugin.Message);
            SetStatus(_godotEditor, godot.EditorBridgeConnected,
                godot.EditorBridgeConnected
                    ? $"Connected {_godot.Settings.EditorHost}:{_godot.Settings.EditorPort}"
                    : $"Not listening on {_godot.Settings.EditorHost}:{_godot.Settings.EditorPort}");
            SetStatus(_godotRuntime, godot.RuntimeBridgeConnected,
                godot.RuntimeBridgeConnected
                    ? $"Connected {_godot.Settings.RuntimeHost}:{_godot.Settings.RuntimePort}"
                    : "Stopped / not connected");
            SetState(_godotTunnel, godot.TunnelStatus.State, godot.TunnelStatus.Summary);
            SetStatus(_godotSkills, godot.SkillsOk, godot.SkillsSummary);
        }
        catch (Exception ex)
        {
            Log("Status refresh failed: " + ex.Message);
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
        await RefreshStatusAsync();
    }

    private async Task StopAllOwnedAsync()
    {
        Log("Stop Hub-Owned requested. External/adopted tunnels will be left untouched.");
        await RunAdapterActionQuietAsync("Stop Godot MCP", _godot.StopAsync);
        await RunAdapterActionQuietAsync("Stop Blender MCP", _blender.StopAsync);
        await RunAdapterActionQuietAsync("Stop Local Files", _localFiles.StopAsync);
        await RefreshStatusAsync();
    }

    private async Task RunAdapterActionQuietAsync(
        string title,
        Func<CancellationToken, Task> action)
    {
        try
        {
            Log(title + " requested.");
            await action(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log(title + " FAILED: " + ex.Message);
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
        }
        catch (Exception ex)
        {
            Log(title + " FAILED: " + ex.Message);
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            await RefreshStatusAsync();
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

    private async Task OpenLocalFilesSettingsAsync()
    {
        var before = await _localFiles.GetStatusAsync();
        using var form = new LocalFilesSettingsForm();
        if (form.ShowDialog(this) != DialogResult.OK)
            return;

        Log("Local Files allowlist saved.");
        if (before.State == AdapterState.Running)
        {
            try
            {
                Log("Restarting Hub-owned Local Files tunnel to apply the new allowlist…");
                await _localFiles.RestartAsync();
            }
            catch (Exception ex)
            {
                Log("Automatic Local Files restart FAILED: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Restart Local Files MCP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        else if (before.State == AdapterState.External)
        {
            MessageBox.Show(
                this,
                "Settings were saved. The current Local Files tunnel is external/adopted, so Hub will not kill it. Restart that tunnel once to load the new allowlist, or stop it and then press Start in Hub.",
                "Restart required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        await RefreshStatusAsync();
    }

    private static void SetStatus(Label label, bool ok, string text)
    {
        label.Text = "● " + text;
        label.ForeColor = ok ? Good : Bad;
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
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Log(message)));
            return;
        }

        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
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

        File.WriteAllText(dialog.FileName, _log.Text);
        Log("Log saved: " + dialog.FileName);
    }

    private static string GetDisplayVersion()
    {
        var version = typeof(MainForm).Assembly.GetName().Version;
        return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
