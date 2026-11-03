using ConstantineHub.Adapters.Nvidia;
using ConstantineHub.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ConstantineHub.SmokeTests;

internal static class JensenAnimationTests
{
    internal static void Run()
    {
        foreach (var tunnel in Enum.GetValues<AdapterState>())
        foreach (var local in new[] { false, true })
        {
            var snapshot = new NvidiaAppSnapshot(local, "http://127.0.0.1/mcp", true,
                new("nvidia-app", "NVIDIA", tunnel, "fixture"), "fixture", local ? 12 : 0);
            var expected = tunnel == AdapterState.Starting ? JensenConnectionState.Connecting
                : local && tunnel is AdapterState.Running or AdapterState.External ? JensenConnectionState.Connected
                : JensenConnectionState.Disconnected;
            Check(JensenAnimationControl.ConnectionState(snapshot) == expected,
                $"Jensen distinguishes local MCP={local} from tunnel={tunnel}");
        }

        var timeline = new JensenAnimationTimeline();
        Check(timeline.Sample(0).Connection == 0, "Disconnected starts without effects");
        timeline.SetState(JensenConnectionState.Connecting, 1);
        Check(timeline.Sample(2).Connection == 0 && timeline.Sample(2).Glow > 0,
            "Connecting pulses gently without dollars or connected rays");
        timeline.SetState(JensenConnectionState.Connected, 3);
        var partial = timeline.Sample(3.3);
        Check(partial.Connection is > 0 and < 1, "Connection fades in rather than jumping");
        timeline.SetState(JensenConnectionState.Connected, 3.4);
        Check(timeline.TransitionCount == 2 && timeline.Sample(3.7).Connection == 1,
            "Repeated connected snapshots do not restart the transition");
        var a = timeline.Sample(4); var b = timeline.Sample(4.5);
        Check(a.LeftAngle != b.LeftAngle && a.RightAngle != b.RightAngle && a.RingAngle != b.RingAngle,
            "Both dollars and the ring move independently");
        Check(b.LeftAngle < a.LeftAngle && b.RightAngle > a.RightAngle,
            "Left dollar rotates counterclockwise; right dollar rotates clockwise");
        Check(timeline.Sample(3.7).BurstStrength > timeline.Sample(10.7).BurstStrength,
            "Initial burst is stronger than periodic bursts");
        timeline.SetState(JensenConnectionState.Disconnected, 5);
        Check(timeline.Sample(5).Connection == 1 && timeline.Sample(5.3).Connection is > 0 and < 1
            && timeline.Sample(5.7).Connection == 0, "Lost connection fades all connected layers out");
        Check(!timeline.NeedsFrames(6), "Stable disconnected state stops animation frames");
    }

    internal static void Rendering()
    {
        var evidence = Environment.GetEnvironmentVariable("HUB_UI_EVIDENCE");
        using var art = new JensenAnimationControl();
        var timeline = new JensenAnimationTimeline();
        timeline.SetState(JensenConnectionState.Connected, 0);
        foreach (var size in new[] { new Size(160, 160), new Size(240, 240), new Size(320, 200), new Size(480, 480) })
        {
            var head = JensenAnimationControl.HeadBounds(size);
            var left = JensenAnimationControl.MapAnchor(head, JensenAnimationControl.LeftEyeAnchor);
            var right = JensenAnimationControl.MapAnchor(head, JensenAnimationControl.RightEyeAnchor);
            Check(Math.Abs((left.X - head.Left) / head.Width - .423f) < .0001f &&
                Math.Abs((right.X - head.Left) / head.Width - .598f) < .0001f && left.Y == right.Y,
                $"Eye anchors stay on the head at {size.Width}x{size.Height}");
            using var disconnected = Render(art, size, new JensenAnimationTimeline().Sample(4));
            using var connected = Render(art, size, timeline.Sample(4));
            using var rotated = Render(art, size, timeline.Sample(4.5));
            Check(Difference(connected, rotated) > 100, "Concurrent animation layers change the rendered frame");
            foreach (var eye in new[] { left, right })
            {
                var x = (int)eye.X; var y = (int)eye.Y;
                Check(Difference(disconnected, connected, new Rectangle(x - 3, y - 3, 7, 7)) > 5,
                    "Dollar is painted at its fixed eye anchor");
            }
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                Directory.CreateDirectory(evidence);
                disconnected.Save(Path.Combine(evidence, $"jensen-{size.Width}x{size.Height}-disconnected.png"));
                connected.Save(Path.Combine(evidence, $"jensen-{size.Width}x{size.Height}-connected.png"));
                rotated.Save(Path.Combine(evidence, $"jensen-{size.Width}x{size.Height}-rotated.png"));
            }
        }
        timeline.SetState(JensenConnectionState.Disconnected, 5);
        using (var fading = Render(art, new(240, 240), timeline.Sample(5.3)))
        {
            if (!string.IsNullOrWhiteSpace(evidence)) fading.Save(Path.Combine(evidence, "jensen-fading.png"));
        }
        timeline.SetState(JensenConnectionState.Connecting, 6);
        using (var connecting = Render(art, new(240, 240), timeline.Sample(7)))
        {
            if (!string.IsNullOrWhiteSpace(evidence)) connecting.Save(Path.Combine(evidence, "jensen-connecting.png"));
        }

        using var host = new Form { ClientSize = new Size(240, 240), ShowInTaskbar = false };
        host.Controls.Add(art); art.Dock = DockStyle.Fill;
        host.Show(); Application.DoEvents();
        art.SetConnectionState(JensenConnectionState.Connected);
        Check(art.FrameTimerEnabled, "Visible connected control enables the bounded frame timer");
        host.Hide(); Application.DoEvents();
        Check(!art.FrameTimerEnabled, "Hidden window suspends rendering");
        host.Show(); host.WindowState = FormWindowState.Minimized; Application.DoEvents();
        Check(!art.FrameTimerEnabled, "Minimized window suspends rendering");
        host.WindowState = FormWindowState.Normal; Application.DoEvents();
        Check(art.FrameTimerEnabled, "Restored window resumes rendering");
        art.Dispose();
        Check(!art.FrameTimerEnabled, "Disposal releases the frame timer");

        using var process = Process.GetCurrentProcess();
        timeline.SetState(JensenConnectionState.Connected, 7);
        // Warm up GDI+ and WinForms before measuring repeated control lifetimes.
        var before = GetGuiResources(process.Handle, 0);
        for (var i = 0; i < 12; i++)
        {
            using var repeated = new JensenAnimationControl();
            using var image = Render(repeated, new(240, 240), timeline.Sample(8));
        }
        var after = GetGuiResources(process.Handle, 0);
        Check(after <= before + 4, $"Repeated create/render/dispose keeps GDI handles bounded ({before} -> {after})");
        using var benchmark = new JensenAnimationControl();
        using var target = new Bitmap(240, 240);
        using var graphics = Graphics.FromImage(target);
        benchmark.RenderFrame(graphics, target.Size, timeline.Sample(8));
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 180; i++) benchmark.RenderFrame(graphics, target.Size, timeline.Sample(8 + i / 30.0));
        Console.WriteLine($"JENSEN_RENDER: {watch.Elapsed.TotalMilliseconds / 180:F2} ms/frame at 240px (30 FPS cap)");
    }

    private static Bitmap Render(JensenAnimationControl art, Size size, JensenAnimationFrame frame)
    {
        var bitmap = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(8, 15, 10));
        art.RenderFrame(graphics, size, frame);
        return bitmap;
    }

    private static int Difference(Bitmap a, Bitmap b, Rectangle? area = null)
    {
        var bounds = area ?? new Rectangle(Point.Empty, a.Size);
        var changed = 0;
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) changed++;
        return changed;
    }

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}
