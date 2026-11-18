using ConstantineHub.Adapters.Claudius;
using ConstantineHub.Core;

namespace ConstantineHub;

/// <summary>Claudius MCP: Claude's own switchable Blender channel, independent of Jarvis tunnels.</summary>
internal sealed partial class MainForm
{
    internal const string ClaudiusTitle = "CLAUDIUS MCP";
    internal const string ClaudiusIconAsset = "ClaudiusIcon.png";

    private readonly ClaudiusAdapter _claudius = new();
    private readonly Label _claudiusChannel = new();
    private readonly Label _claudiusClient = new();
    private readonly Label _claudiusBridge = new();
    private readonly Label _claudiusActivity = new();
    private Control? _claudiusCard;
    private ClaudiusSnapshot? _latestClaudius;

    /// <summary>Legacy-layout card; the UI shell places it above Local Files.</summary>
    internal Control ClaudiusCard => _claudiusCard ??= BuildClaudiusCard();

    private Control BuildClaudiusCard()
    {
        var card = BuildCard(ClaudiusTitle, 4, out var content, out var actions);
        AddStatusRow(content, 0, "Channel", _claudiusChannel);
        AddStatusRow(content, 1, "Claude Desktop", _claudiusClient);
        AddStatusRow(content, 2, "Blender bridge", _claudiusBridge);
        AddStatusRow(content, 3, "Activity", _claudiusActivity);

        actions.Controls.Add(MakeButton("Start", async (_, _) => await RunAdapterActionAsync("Open Claudius channel", _claudius.StartAsync), primary: true));
        AddTunnelActions(actions, _claudius, _claudiusChannel, "Claudius channel");
        _tunnelActions[_claudius.Id].Stop.Name = "claudius-stop";
        _tunnelActions[_claudius.Id].Restart.Name = "claudius-restart";
        actions.Controls.Add(MakeButton("Doctor", async (_, _) => await ShowClaudiusDoctorAsync()));
        actions.Controls.Add(MakeButton("Claude Setup", async (_, _) =>
            await RunAdapterActionAsync("Register Claudius in Claude Desktop", _claudius.RepairRegistrationAsync)));
        return card;
    }

    private async Task RefreshClaudiusAsync()
    {
        var s = _latestClaudius = await _claudius.GetSnapshotAsync();
        if (IsDisposed || Disposing) return;

        SetState(_claudiusChannel, s.Status.State, s.Status.Summary);
        UpdateTunnelActions(_claudius.Id, s.Status.State);
        SetOptionalStatus(_claudiusClient, s.GatewayLive, s.GatewayLive
            ? $"Connected · {s.Gateway!.Client ?? "Claude"} · gateway pid {s.Gateway.GatewayPid}"
            : s.Registration.Current ? "Registered · not running (open or restart Claude Desktop)" : s.Registration.Message);
        SetOptionalStatus(_claudiusBridge, s.BridgeConnected, s.BridgeConnected
            ? $"Connected {s.BridgeEndpoint} (shared, authenticated)"
            : $"Not listening on {s.BridgeEndpoint}");
        _claudiusActivity.Text = s.Gateway is null
            ? "● No Claude calls yet"
            : $"● {s.Gateway.Calls} forwarded · {s.Gateway.Blocked} refused" +
              (s.Gateway.LastTool is null ? "" : $" · last {s.Gateway.LastTool} {s.Gateway.LastCallUtc?.ToLocalTime():HH:mm}");
        _claudiusActivity.ForeColor = Muted;
        _tunnelActionTips.SetToolTip(_claudiusClient, s.Registration.ConfigPath ?? "Claude Desktop config not found");

        SetBridgeRow(_claudius.Id, s.Status.State, s.Status.State switch
        {
            AdapterState.Running => s.GatewayLive ? "channel open · Claude connected" : "channel open · Claude idle",
            AdapterState.Stopped => "channel closed",
            _ => "setup required"
        });
    }

    private async Task ShowClaudiusDoctorAsync()
    {
        string report;
        try { report = await _claudius.DoctorAsync(); }
        catch (Exception ex) { report = "Claudius Doctor failed: " + ex.Message; }
        Log(report);
        using var dialog = new Form
        {
            Text = "Claudius MCP — Doctor",
            Icon = LoadClaudiusIcon() ?? this.Icon,
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(860, 520),
            MinimizeBox = false,
            BackColor = Color.FromArgb(18, 26, 34),
            ForeColor = Color.Gainsboro,
            ShowInTaskbar = false
        };
        dialog.Controls.Add(new TextBox
        {
            Text = report.Replace("\n", Environment.NewLine),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 17, 21),
            ForeColor = Color.Gainsboro,
            Font = new Font("Cascadia Mono", 9.5F)
        });
        dialog.ShowDialog(this);
        await RefreshStatusAsync();
    }

    internal static Icon? LoadClaudiusIcon()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "ClaudiusIcon.ico"),
                     Path.Combine(AppContext.BaseDirectory, "Assets", "ClaudiusIcon.ico")
                 })
        {
            try { if (File.Exists(path)) return new Icon(path); }
            catch (Exception ex) when (ex is ArgumentException or IOException) { }
        }
        return null;
    }

    internal static Image? LoadClaudiusGlyph(int size)
    {
        using var icon = LoadClaudiusIcon();
        if (icon is null) return null;
        using var sized = new Icon(icon, new Size(size, size));
        return sized.ToBitmap();
    }
}
