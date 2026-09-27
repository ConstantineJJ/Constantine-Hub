using System.Diagnostics;
using ConstantineHub.Adapters.Blender;
using ConstantineHub.Adapters.LocalFiles;
using ConstantineHub.Core;

namespace ConstantineHub;

internal sealed class MainForm : Form
{
    private readonly LocalFilesAdapter _localFiles = new();
    private readonly BlenderAdapter _blender = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1500 };
    private readonly RichTextBox _log = new();

    private readonly Label _localTunnel = new();
    private readonly Label _localRoots = new();
    private readonly Label _localAccess = new();
    private readonly Label _blenderApp = new();
    private readonly Label _blenderBridge = new();
    private readonly Label _blenderTunnel = new();
    private readonly Label _blenderSkills = new();
    private readonly Label _godotStatus = new();

    private bool _refreshing;

    private static readonly Color Good = Color.FromArgb(92, 201, 120);
    private static readonly Color Bad = Color.FromArgb(232, 104, 104);
    private static readonly Color Warn = Color.FromArgb(232, 188, 104);
    private static readonly Color Muted = Color.FromArgb(170, 174, 184);

    internal MainForm()
    {
        Text = "Constantine Hub";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 760);
        Size = new Size(1120, 840);
        BackColor = Color.FromArgb(22, 24, 29);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 10F);

        BuildUi();
        _localFiles.LogLine += LogFromAnyThread;
        _blender.LogLine += LogFromAnyThread;
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _statusTimer.Start();

        Shown += async (_, _) =>
        {
            Log("Constantine Hub v0.1 skeleton started.");
            Log("Local Files MCP is the first managed adapter. Blender migration is diagnostic-only in this pass.");
            await RefreshStatusAsync();
        };

        FormClosing += (_, _) =>
        {
            _statusTimer.Stop();
            _localFiles.Dispose();
            _blender.Dispose();
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 166));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label
        {
            Text = "Constantine Hub",
            Font = new Font("Segoe UI Semibold", 22F),
            AutoSize = true,
            Location = new Point(0, 2)
        });
        header.Controls.Add(new Label
        {
            Text = $"Local MCP control plane  •  v{Application.ProductVersion}",
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(3, 48)
        });
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
        globalActions.Controls.Add(MakeButton("Refresh", async (_, _) => await RefreshStatusAsync()));
        globalActions.Controls.Add(MakeButton("Save Log", (_, _) => SaveLog()));
        root.Controls.Add(globalActions, 0, 4);

        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BackColor = Color.FromArgb(15, 17, 21);
        _log.ForeColor = Color.Gainsboro;
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.Font = new Font("Cascadia Mono", 9.5F);
        root.Controls.Add(_log, 0, 5);
    }

    private Control BuildLocalFilesCard()
    {
        var card = BuildCard("LOCAL FILES MCP", out var content, out var actions);
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
        var card = BuildCard("BLENDER MCP", out var content, out var actions);
        content.RowCount = 4;
        content.RowStyles.Clear();
        for (var i = 0; i < 4; i++)
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));

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
        var card = BuildCard("GODOT MCP", out var content, out var actions);
        content.RowCount = 1;
        content.RowStyles.Clear();
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddStatusRow(content, 0, "Status", _godotStatus);
        actions.Controls.Add(new Label
        {
            Text = "Rebuild pending — future adapter will install/update the Godot project plugin automatically.",
            AutoSize = true,
            ForeColor = Muted,
            Margin = new Padding(0, 8, 0, 0)
        });
        return card;
    }

    private static Panel BuildCard(
        string title,
        out TableLayoutPanel content,
        out FlowLayoutPanel actions)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 33, 40),
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 10)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        card.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 11F),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++)
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333F));
        layout.Controls.Add(content, 0, 1);

        actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 6, 0, 0)
        };
        layout.Controls.Add(actions, 0, 2);
        return card;
    }

    private static void AddStatusRow(TableLayoutPanel table, int row, string name, Label value)
    {
        table.Controls.Add(new Label
        {
            Text = name,
            Dock = DockStyle.Fill,
            ForeColor = Color.Silver,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        value.Dock = DockStyle.Fill;
        value.Text = "● Checking…";
        value.ForeColor = Muted;
        value.TextAlign = ContentAlignment.MiddleLeft;
        table.Controls.Add(value, 1, row);
    }

    private static Button MakeButton(string text, EventHandler handler, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Width = 112,
            Height = 34,
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(55, 106, 175) : Color.FromArgb(42, 46, 55),
            ForeColor = Color.White
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 74, 85);
        button.Click += handler;
        return button;
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
                blender.BridgeConnected ? $"Connected {blender.BridgeEndpoint}" : $"Not listening on {blender.BridgeEndpoint}");
            SetState(_blenderTunnel, blender.TunnelStatus.State, blender.TunnelStatus.Summary);
            SetStatus(_blenderSkills, blender.Skills.Success,
                blender.Skills.Success
                    ? $"{blender.Skills.VerifiedCount}/{blender.Skills.SourceCount} verified"
                    : blender.Skills.Message);

            SetState(_godotStatus, AdapterState.NotConfigured, "Rebuild pending; no runtime is attached.");
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
}
