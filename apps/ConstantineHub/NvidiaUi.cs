using ConstantineHub.Adapters.Nvidia;
using ConstantineHub.Core;

namespace ConstantineHub;

internal sealed partial class MainForm
{
    private readonly NvidiaAppAdapter _nvidia = new();
    private readonly Label _nvidiaLocal = new() { AutoEllipsis = true, Dock = DockStyle.Fill };
    private readonly Label _nvidiaTunnel = new() { AutoEllipsis = true, Dock = DockStyle.Fill };
    private readonly Label _nvidiaDetail = new() { AutoEllipsis = true, Dock = DockStyle.Fill };
    private Button? _nvidiaStop;
    private Button? _nvidiaRestart;
    private NvidiaAppSnapshot? _latestNvidia;
    private JensenAnimationControl? _nvidiaArt;
    private int _nvidiaConnectionOperations;

    internal void AttachV054Features()
    {
        int S(int value) => (int)Math.Round(value * Math.Max(1f, DeviceDpi / 96f));
        var shell = Controls.OfType<TableLayoutPanel>().First(p => p.ColumnCount == 3);
        var navigation = (ScrollableControl)shell.GetControlFromPosition(0, 0)!;
        var nav = HelperDescendants<TableLayoutPanel>(navigation).First(p => p.RowCount >= 8 && p.ColumnCount == 1);
        var top = shell.GetControlFromPosition(1, 0)!;
        var home = HelperDescendants<Button>(navigation).First(b => b.Text == "Home");
        var homeHost = HelperDescendants<Panel>(top).First(p => p.Name == "TopNavigationHost");
        home.Parent!.Controls.Remove(home);
        homeHost.Controls.Add(home); home.Dock = DockStyle.Left; home.Width = S(95); home.Margin = Padding.Empty;
        nav.RowStyles[2].Height = 0;
        if (nav.GetControlFromPosition(0, 1) is Label section) section.Text = "UTILITY";

        // Reuse the real selector and actions, so project switching keeps its existing validation.
        var project = HelperDescendants<TableLayoutPanel>(shell).First(p => p.Name == "GodotProjectSelector");
        var oldParent = project.Parent!;
        var caption = project.Controls.OfType<Label>().First(l => l.Text == "GODOT PROJECT");
        var picker = _projectPicker!;
        var actions = project.Controls.OfType<FlowLayoutPanel>().Single();
        var hint = project.Controls.OfType<Label>().First(l => !ReferenceEquals(l, caption));
        project.Controls.Remove(caption); project.Controls.Remove(picker); project.Controls.Remove(actions);
        project.Controls.Remove(hint); hint.Dispose();
        project.ColumnCount = 3; project.RowCount = 1; project.ColumnStyles.Clear(); project.RowStyles.Clear();
        project.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(128)));
        project.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        project.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(238)));
        project.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        project.Padding = new Padding(0, S(4), 0, S(4));
        project.Controls.Add(caption, 0, 0); project.Controls.Add(picker, 1, 0); project.Controls.Add(actions, 2, 0);
        caption.Font = new Font("Segoe UI Semibold", 9F);
        caption.TextAlign = ContentAlignment.MiddleLeft;
        picker.Anchor = AnchorStyles.Left | AnchorStyles.Right; picker.Dock = DockStyle.None;
        actions.Dock = DockStyle.Fill; actions.Padding = Padding.Empty;
        oldParent.Controls.Remove(project);
        var topLayout = top.Controls.OfType<TableLayoutPanel>().First();
        topLayout.RowCount = 2; topLayout.RowStyles.Clear();
        topLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(46)));
        topLayout.Controls.Add(project, 0, 1); topLayout.SetColumnSpan(project, 3);
        project.Dock = DockStyle.Fill; project.Margin = Padding.Empty;

        var utility = BuildNvidiaUtilityCard();
        nav.Controls.Add(utility, 0, 5);
        nav.RowStyles[5].SizeType = SizeType.Absolute;
        nav.RowStyles[5].Height = S(370);
        navigation.AutoScroll = true;
        nav.Dock = DockStyle.Top; nav.AutoSize = true; nav.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        void FitUtility()
        {
            var width = Math.Max(S(160), navigation.ClientSize.Width - navigation.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            nav.Width = width;
            var imageHeight = Math.Max(1, width - utility.Padding.Horizontal);
            utility.Height = imageHeight + (int)Math.Ceiling(imageHeight * 2.0 / 3.0) + S(8);
            nav.RowStyles[5].Height = utility.Height;
        }
        navigation.SizeChanged += (_, _) => FitUtility(); FitUtility();

        var services = HelperDescendants<TableLayoutPanel>(shell).First(p => p.Name == "ActiveBridges");
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(64)));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, S(25))); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = "NVIDIA App", Dock = DockStyle.Fill }, 0, 0);
        var railStop = MakeButton("Stop", async (_, _) => await RunAdapterActionAsync("Stop NVIDIA App MCP", _nvidia.StopAsync));
        railStop.Name = "rail-stop-nvidia-app"; railStop.Enabled = false; railStop.Dock = DockStyle.Fill;
        railStop.Font = new Font("Segoe UI", 9F); railStop.Margin = new Padding(2);
        row.Controls.Add(railStop, 1, 0); row.SetRowSpan(railStop, 2);
        var status = new Label { Text = "Checking…", AutoEllipsis = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5F) };
        row.Controls.Add(status, 0, 1); _bridgeRows["nvidia-app"] = (status, railStop);
        services.RowCount++; services.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        services.Controls.Add(row, 0, services.RowCount - 1); services.Height += S(68);
    }

    private Panel BuildNvidiaUtilityCard()
    {
        int S(int value) => (int)Math.Round(value * Math.Max(1f, DeviceDpi / 96f));
        var card = new Panel { Name = "NvidiaUtilityCard", Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8),
            Padding = new Padding(4), BackColor = Color.FromArgb(8, 15, 10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        _nvidiaArt = new JensenAnimationControl { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = card.BackColor };
        layout.Controls.Add(_nvidiaArt, 0, 0);
        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
        foreach (var height in new[] { 20, 19, 19, 32 }) details.RowStyles.Add(new RowStyle(SizeType.Absolute, S(height)));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.Controls.Add(new Label { Text = "NVIDIA MCP", ForeColor = Color.FromArgb(191, 252, 164), Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _nvidiaLocal.Font = _nvidiaTunnel.Font = _nvidiaDetail.Font = new Font("Segoe UI", 8F);
        _nvidiaLocal.Text = "Checking local MCP…"; _nvidiaTunnel.Text = "Checking tunnel…";
        details.Controls.Add(_nvidiaLocal, 0, 1); details.Controls.Add(_nvidiaTunnel, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var start = MakeButton("Start", async (_, _) => await RunAdapterActionAsync("Start NVIDIA App MCP", ct => ConnectNvidiaAsync(false, ct)));
        _nvidiaStop = MakeButton("Stop", async (_, _) => await RunAdapterActionAsync("Stop NVIDIA App MCP", _nvidia.StopAsync));
        _nvidiaRestart = MakeButton("↻", async (_, _) => await RunAdapterActionAsync("Restart NVIDIA App MCP", ct => ConnectNvidiaAsync(true, ct)));
        _nvidiaRestart.AccessibleName = "Restart NVIDIA App MCP";
        var doctor = MakeButton("Doctor", async (_, _) => await RunAdapterActionAsync("NVIDIA App MCP Doctor", async ct => Log(await _nvidia.DoctorAsync(ct))));
        foreach (var button in new[] { start, _nvidiaStop, _nvidiaRestart, doctor })
        {
            button.Name = "nvidia-" + (ReferenceEquals(button, _nvidiaRestart) ? "restart" : button.Text.ToLowerInvariant());
            button.Width = S(button.Text == "↻" ? 28 : button.Text == "Doctor" ? 53 : 43);
            button.Height = S(30); button.MinimumSize = Size.Empty; button.AutoSize = false;
            button.Font = new Font("Segoe UI", 8F); button.Padding = Padding.Empty;
            button.Margin = new Padding(0, 0, S(2), 0); button.BackColor = Color.FromArgb(15, 25, 17);
            button.ForeColor = Color.FromArgb(191, 252, 164); actions.Controls.Add(button);
        }
        details.Controls.Add(actions, 0, 3); details.Controls.Add(_nvidiaDetail, 0, 4);
        layout.Controls.Add(details, 0, 1); card.Controls.Add(layout); return card;
    }

    private async Task RefreshNvidiaAsync(CancellationToken cancellationToken = default)
    {
        _latestNvidia = await _nvidia.GetSnapshotAsync(cancellationToken);
        if (IsDisposed || Disposing) return;
        _nvidiaArt?.SetConnectionState(_nvidiaConnectionOperations > 0 ? JensenConnectionState.Connecting
            : JensenAnimationControl.ConnectionState(_latestNvidia));
        _nvidiaLocal.Text = _latestNvidia.HttpAvailable ? $"Local MCP ready · {_latestNvidia.ToolCount} tools" : "Local MCP unavailable";
        _nvidiaLocal.ForeColor = _latestNvidia.HttpAvailable ? Good : Warn;
        _nvidiaTunnel.Text = _latestNvidia.TunnelStatus.Summary;
        _nvidiaDetail.Text = _latestNvidia.HttpAvailable ? "NVIDIA's local service remains externally managed." : _latestNvidia.HttpMessage;
        _nvidiaDetail.ForeColor = Muted;
        _tunnelActionTips.SetToolTip(_nvidiaLocal, _latestNvidia.HttpEndpoint + "\n" + _latestNvidia.HttpMessage);
        _tunnelActionTips.SetToolTip(_nvidiaTunnel, _latestNvidia.TunnelStatus.Summary);
        _tunnelActionTips.SetToolTip(_nvidiaDetail, _nvidiaDetail.Text);
        var controlled = _latestNvidia.TunnelStatus.State == AdapterState.Running;
        if (_nvidiaStop is not null) _nvidiaStop.Enabled = controlled;
        if (_nvidiaRestart is not null) _nvidiaRestart.Enabled = _latestNvidia.TunnelStatus.State != AdapterState.External;
        SetBridgeRow("nvidia-app", _latestNvidia.TunnelStatus.State,
            _latestNvidia.HttpAvailable ? "local MCP ready" : "local MCP offline");
    }

    private async Task StartNvidiaIfConfiguredAsync()
    {
        var status = await _nvidia.GetStatusAsync();
        if (status.State != AdapterState.NotConfigured)
            await RunAdapterActionQuietAsync("Start NVIDIA App MCP", ct => ConnectNvidiaAsync(false, ct));
        else Log("NVIDIA App MCP: configure the dedicated constantine-nvidia tunnel profile to enable remote access.");
    }

    private async Task ConnectNvidiaAsync(bool restart, CancellationToken cancellationToken)
    {
        _nvidiaConnectionOperations++;
        _nvidiaArt?.SetConnectionState(JensenConnectionState.Connecting);
        try
        {
            if (restart) await _nvidia.RestartAsync(cancellationToken);
            else await _nvidia.StartAsync(cancellationToken);
        }
        finally { _nvidiaConnectionOperations--; }
    }
}

internal sealed class ConHubBrand : Control
{
    internal static readonly Color ConColor = ColorTranslator.FromHtml("#FC6064");
    internal static readonly Color HubColor = ColorTranslator.FromHtml("#BFFCA4");
    internal ConHubBrand()
    {
        DoubleBuffered = true; AccessibleName = "ConHub."; AccessibleRole = AccessibleRole.StaticText;
        Font = new Font("Segoe UI Semibold", 24F); ForeColor = HubColor;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var con = TextRenderer.MeasureText(e.Graphics, "Con", Font, Size.Empty, flags);
        var hub = TextRenderer.MeasureText(e.Graphics, "Hub.", Font, Size.Empty, flags);
        var x = Math.Max(0, (ClientSize.Width - con.Width - hub.Width) / 2);
        var y = Math.Max(0, (ClientSize.Height - Math.Max(con.Height, hub.Height)) / 2);
        TextRenderer.DrawText(e.Graphics, "Con", Font, new Point(x, y), ConColor, flags);
        TextRenderer.DrawText(e.Graphics, "Hub.", Font, new Point(x + con.Width, y), HubColor, flags);
    }
}
