using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ConstantineHub.Adapters.Nvidia;
using ConstantineHub.Core;

namespace ConstantineHub;

internal sealed class JensenAnimationControl : Control
{
    internal static readonly string[] AssetNames = ["Jensen_Connected.png", "Jensen_Disconnected.png",
        "Jensen_Dollar.png", "Jensen_RaysRing.png", "Jensen_RaysBurst.png", "Jensen_Sparkle.png"];
    // Coordinates in the supplied 1254 x 1254 head canvas, before any window/DPI scaling.
    internal static readonly PointF LeftEyeAnchor = new(.423f, .459f);
    internal static readonly PointF RightEyeAnchor = new(.598f, .459f);
    private static readonly PointF[] RayTipAnchors = [new(.22f, .12f), new(.74f, .11f), new(.90f, .55f), new(.49f, .94f)];
    private readonly JensenAnimationTimeline _timeline = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly ImageAttributes _opacity = new();
    private readonly ColorMatrix _colorMatrix = new();
    private readonly Bitmap[] _originals;
    private Bitmap[]? _scaled;
    private int _cacheSide;
    private Form? _host;
    private bool _disposed;
    internal JensenAnimationTimeline Timeline => _timeline;
    internal bool FrameTimerEnabled => _timer.Enabled;

    internal JensenAnimationControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Name = "NvidiaJensenArt";
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "NVIDIA MCP — disconnected";
        TabStop = false;
        var originals = new List<Bitmap>();
        try
        {
            foreach (var name in AssetNames)
            {
                // Never search dist, the working directory, or legacy artwork as a fallback.
                using var source = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Assets", "JensenAnimation", name));
                originals.Add(source.Clone(name is "Jensen_Dollar.png" or "Jensen_Sparkle.png"
                    ? VisibleBounds(source) : new Rectangle(Point.Empty, source.Size), PixelFormat.Format32bppPArgb));
            }
            _originals = originals.ToArray();
        }
        catch
        {
            foreach (var bitmap in originals) bitmap.Dispose();
            _timer.Dispose(); _opacity.Dispose();
            throw;
        }
        _timer.Tick += OnFrame;
    }

    internal static JensenConnectionState ConnectionState(NvidiaAppSnapshot snapshot) => snapshot.TunnelStatus.State switch
    {
        AdapterState.Starting => JensenConnectionState.Connecting,
        AdapterState.Running or AdapterState.External when snapshot.HttpAvailable => JensenConnectionState.Connected,
        _ => JensenConnectionState.Disconnected
    };

    internal void SetSnapshot(NvidiaAppSnapshot snapshot) => SetConnectionState(ConnectionState(snapshot));

    internal void SetConnectionState(JensenConnectionState state)
    {
        if (_disposed || state == _timeline.State) return;
        _timeline.SetState(state, _clock.Elapsed.TotalSeconds);
        AccessibleName = "NVIDIA MCP — " + state.ToString().ToLowerInvariant();
        UpdateFrameTimer();
        Invalidate();
    }

    internal static RectangleF HeadBounds(Size size)
    {
        var side = Math.Min(size.Width, size.Height) * .92f;
        return new((size.Width - side) / 2, (size.Height - side) / 2, side, side);
    }

    internal static PointF MapAnchor(RectangleF head, PointF anchor)
        => new(head.X + anchor.X * head.Width, head.Y + anchor.Y * head.Height);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        RenderFrame(e.Graphics, ClientSize, _timeline.Sample(_clock.Elapsed.TotalSeconds));
    }

    internal void RenderFrame(Graphics graphics, Size size, JensenAnimationFrame frame)
    {
        if (_disposed || size.Width < 2 || size.Height < 2) return;
        EnsureCache(size);
        var images = _scaled!;
        var head = HeadBounds(size);
        var center = MapAnchor(head, new(.5f, .52f));
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var rayWidth = head.Width * 1.32f;
        DrawSprite(graphics, images[3], center, rayWidth * frame.RingScale, frame.RingAngle,
            frame.Connection * frame.RingOpacity);
        if (frame.Glow > .001f)
            DrawSprite(graphics, images[4], center, head.Width * 1.22f, 0, frame.Glow);
        if (frame.BurstAge < 1.2f && frame.Connection > .001f)
        {
            var age = frame.BurstAge / 1.2f;
            DrawSprite(graphics, images[4], center, head.Width * (1.02f + age * .45f),
                -frame.RingAngle * .6f, frame.Connection * frame.BurstStrength * (1 - age) * (1 - age));
        }
        // Opaque base + alpha happy face avoids a hole in the head halfway through a dissolve.
        DrawImage(graphics, images[1], head, 1);
        DrawImage(graphics, images[0], head, frame.Connection);
        DrawSprite(graphics, images[2], MapAnchor(head, LeftEyeAnchor), head.Width * .122f,
            frame.LeftAngle, frame.Connection);
        DrawSprite(graphics, images[2], MapAnchor(head, RightEyeAnchor), head.Width * .122f,
            frame.RightAngle, frame.Connection);
        if (frame.Connection > .001f)
        {
            for (var i = 0; i < 4; i++)
            {
                var phase = (frame.Time + i * .83) % 3.7 / 3.7;
                var alpha = (float)Math.Pow(Math.Sin(phase * Math.PI), 6) * frame.Connection * .9f;
                var angle = frame.RingAngle * Math.PI / 180;
                var tip = RayTipAnchors[i];
                var x = (tip.X - .5f) * rayWidth * frame.RingScale;
                var y = (tip.Y - .5f) * rayWidth * frame.RingScale * images[3].Height / images[3].Width;
                var point = new PointF(center.X + x * (float)Math.Cos(angle) - y * (float)Math.Sin(angle),
                    center.Y + x * (float)Math.Sin(angle) + y * (float)Math.Cos(angle));
                DrawSprite(graphics, images[5], point, head.Width * (.065f + alpha * .035f),
                    (float)(frame.Time * -23 + i * 41), alpha);
            }
        }
    }

    private void DrawSprite(Graphics graphics, Bitmap image, PointF center, float width, float angle, float alpha)
    {
        if (alpha <= .001f) return;
        var saved = graphics.Save();
        try
        {
            graphics.TranslateTransform(center.X, center.Y);
            graphics.RotateTransform(angle);
            var height = width * image.Height / image.Width;
            DrawImage(graphics, image, new(-width / 2, -height / 2, width, height), alpha);
        }
        finally { graphics.Restore(saved); }
    }

    private void DrawImage(Graphics graphics, Bitmap image, RectangleF destination, float alpha)
    {
        if (alpha <= .001f) return;
        _colorMatrix.Matrix33 = Math.Clamp(alpha, 0, 1);
        _opacity.SetColorMatrix(_colorMatrix);
        // The floating-point overload with three corners keeps the subpixel pivot fixed.
        PointF[] corners = [new(destination.Left, destination.Top), new(destination.Right, destination.Top),
            new(destination.Left, destination.Bottom)];
        graphics.DrawImage(image, corners, new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel, _opacity);
    }

    private void EnsureCache(Size size)
    {
        var side = Math.Clamp(Math.Max(size.Width, size.Height), 32, 768);
        if (_scaled is not null && _cacheSide == side) return;
        var next = new List<Bitmap>();
        try
        {
            for (var i = 0; i < _originals.Length; i++)
            {
                var source = _originals[i];
                var maxSide = side * (i == 2 ? .25f : i == 5 ? .15f : 1.4f);
                var ratio = maxSide / Math.Max(source.Width, source.Height);
                var bitmap = new Bitmap(Math.Max(1, (int)Math.Ceiling(source.Width * ratio)),
                    Math.Max(1, (int)Math.Ceiling(source.Height * ratio)), PixelFormat.Format32bppPArgb);
                next.Add(bitmap);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, new Rectangle(Point.Empty, bitmap.Size));
            }
        }
        catch { foreach (var bitmap in next) bitmap.Dispose(); throw; }
        if (_scaled is not null) foreach (var bitmap in _scaled) bitmap.Dispose();
        _scaled = next.ToArray(); _cacheSide = side;
    }

    private static Rectangle VisibleBounds(Bitmap image)
    {
        var area = new Rectangle(Point.Empty, image.Size);
        var bits = image.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[image.Width * 4];
            var left = image.Width; var right = -1; var top = image.Height; var bottom = -1;
            for (var y = 0; y < image.Height; y++)
            {
                Marshal.Copy(bits.Scan0 + y * bits.Stride, row, 0, row.Length);
                for (var x = 0; x < image.Width; x++)
                {
                    if (row[x * 4 + 3] <= 8) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
            }
            return right >= left ? Rectangle.FromLTRB(left, top, right + 1, bottom + 1) : area;
        }
        finally { image.UnlockBits(bits); }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _host = FindForm();
        if (_host is not null)
        {
            _host.Resize += HostResize;
            _host.VisibleChanged += HostResize;
        }
        UpdateFrameTimer();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _timer.Stop();
        DetachHost();
        _host = null;
        base.OnHandleDestroyed(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateFrameTimer();
    }

    private void HostResize(object? sender, EventArgs e) => UpdateFrameTimer();
    private void DetachHost()
    {
        if (_host is null) return;
        _host.Resize -= HostResize;
        _host.VisibleChanged -= HostResize;
    }
    private void OnFrame(object? sender, EventArgs e) { Invalidate(); UpdateFrameTimer(); }
    private void UpdateFrameTimer()
    {
        if (_disposed) return;
        _timer.Enabled = IsHandleCreated && Visible && _host?.Visible != false && _host?.WindowState != FormWindowState.Minimized
            && _timeline.NeedsFrames(_clock.Elapsed.TotalSeconds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            DetachHost();
            _timer.Stop(); _timer.Tick -= OnFrame; _timer.Dispose();
            _opacity.Dispose();
            foreach (var bitmap in _originals) bitmap.Dispose();
            if (_scaled is not null) foreach (var bitmap in _scaled) bitmap.Dispose();
            _scaled = null;
        }
        base.Dispose(disposing);
    }
}
