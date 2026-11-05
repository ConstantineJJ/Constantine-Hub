using System.Drawing.Imaging;
using ConstantineHub.Adapters.Godot;
using ConstantineHub.Core;

namespace ConstantineHub;

internal sealed partial class MainForm
{
    private ReconnectFallback? _reconnect;
    private ReconnectButton? _reconnectButton;

    private void InitializeReconnect()
    {
        GodotAdapterSnapshot? latest = null;
        _reconnect = new ReconnectFallback(async token =>
        {
            var snapshot = await _godot.GetSnapshotAsync(token);
            latest = snapshot;
            return snapshot.RecoveryChecksPassed;
        }, _godot.RecoverAsync, restoredDetails: () =>
            "Tunnel identity/channel/PID checks passed; editor project identity responded. " +
            "MCP readiness is inferred, not an MCP roundtrip. Runtime bridge: " +
            (latest?.RuntimeBridgeConnected == true ? "responded for the selected project." :
                (latest?.RuntimeBridgeIssue ?? "not checked") + " Runtime is optional when the game is stopped."));
        _reconnect.LogLine += message => Log("ReConnect: " + message);
        _godot.LogLine += _reconnect.ObserveLog;
        _reconnect.Changed += () =>
        {
            if (IsDisposed || Disposing) return;
            _reconnectButton?.SetState(_reconnect.Enabled, _reconnect.State, _reconnect.Attempts);
        };
    }

    private void StopReconnectForUserAction(Func<CancellationToken, Task> action)
    {
        if (action.Target is GodotAdapter && action.Method.Name == nameof(IHubAdapter.StopAsync))
            StopReconnectForUserStop();
    }

    private void StopReconnectForUserStop()
    {
        if (_reconnect?.Enabled != true) return;
        _reconnect.Disable();
        Log("ReConnect disabled by explicit Godot Stop.");
    }

    internal Control CreateReconnectButton()
    {
        _reconnectButton = new ReconnectButton
        {
            Name = "godot-reconnect", Text = "ReConnect", AccessibleName = "ReConnect — off",
            Size = new Size((int)(158 * DeviceDpi / 96f), (int)(58 * DeviceDpi / 96f)),
            Margin = new Padding(0, 0, (int)(38 * DeviceDpi / 96f), 0),
            Anchor = AnchorStyles.Right,
            Font = new Font("Segoe UI", 9.5F), ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(43, 57, 70)
        };
        _reconnectButton.Click += async (_, _) =>
        {
            if (_reconnect is null) return;
            if (!_reconnect.Enabled || _reconnect.State == ReconnectState.Waiting)
            {
                _reconnect.Enable();
                Log("ReConnect enabled; nine-attempt fallback armed.");
                await _reconnect.CheckAsync();
            }
            else { _reconnect.Disable(); Log("ReConnect disabled."); }
        };
        return _reconnectButton;
    }
}

internal sealed class ReconnectButton : Button
{
    private readonly Image? _arrows;
    private bool _enabled;
    private ReconnectState _state;

    internal ReconnectButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        var path = Path.Combine(AppContext.BaseDirectory, "RCicon.png");
        if (File.Exists(path))
        {
            using var source = Image.FromFile(path);
            _arrows = new Bitmap(source);
        }
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    internal void SetState(bool enabled, ReconnectState state, int attempts)
    {
        _enabled = enabled; _state = state;
        AccessibleName = $"ReConnect — {(!enabled ? "off" : state.ToString())}, {attempts}/9";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        var scale = DeviceDpi / 96f;
        var face = new Rectangle((int)(22 * scale), (int)(13 * scale), Width - (int)(44 * scale), Height - (int)(26 * scale));
        using var fill = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(fill, face);
        using var border = new Pen(Focused ? Color.Gainsboro : Color.FromArgb(65, 80, 92));
        e.Graphics.DrawRectangle(border, face);
        if (_enabled && _arrows is not null)
        {
            using var attributes = new ImageAttributes();
            if (_state != ReconnectState.Observing)
            {
                // Runtime tint of the supplied artwork, preserving its alpha.
                var yellow = _state == ReconnectState.Waiting;
                attributes.SetColorMatrix(new ColorMatrix(new[] {
                    new float[] { 0, 0, 0, 0, 0 }, new float[] { 1, yellow ? .85f : 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 }
                }));
            }
            var height = Math.Min(Height, Width * _arrows.Height / _arrows.Width);
            e.Graphics.DrawImage(_arrows, new Rectangle(0, (Height - height) / 2, Width, height),
                0, 0, _arrows.Width, _arrows.Height, GraphicsUnit.Pixel, attributes);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, face, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, face);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _arrows?.Dispose();
        base.Dispose(disposing);
    }
}
