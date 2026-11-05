using System.Reflection;
using System.Diagnostics;
using ConstantineHub.Adapters.Godot;
using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class GodotRecoveryTests
{
    internal static void Run() => Task.Run(async () =>
    {
        await GodotDiagnosticsTests.RunAsync();
        await SerializesAndCancelsAsync();
    }).GetAwaiter().GetResult();

    private static async Task SerializesAndCancelsAsync()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "conhub-godot-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var names = new[] { "CONSTANTINE_GODOT_MCP_CONFIG", "TUNNEL_CLIENT_PROFILE_DIR", "TUNNEL_CLIENT_PROFILE_FILE" };
        var previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            // Missing config/project and an empty private profile directory guarantee no live engine/process work,
            // even if a future regression accidentally bypasses the operation gate.
            Environment.SetEnvironmentVariable(names[0], Path.Combine(fixture, "absent-config.json"));
            Environment.SetEnvironmentVariable(names[1], fixture);
            Environment.SetEnvironmentVariable(names[2], null);
            using (var alreadyCanceled = new CancellationTokenSource())
            {
                alreadyCanceled.Cancel();
                try
                {
                    await ProcessCapture.RunAsync(Path.Combine(fixture, "must-not-start.exe"), [], alreadyCanceled.Token).ConfigureAwait(false);
                    throw new Exception("Pre-canceled process capture unexpectedly completed.");
                }
                catch (OperationCanceledException) { }
                Check(true, "pre-canceled Doctor/setup requests never reach process creation");
            }
            var marker = Path.Combine(fixture, "owned-doctor.pid");
            var script = "Set-Content -LiteralPath '" + marker.Replace("'", "''") + "' -Value $PID; Start-Sleep -Seconds 30";
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            using (var runningCancellation = new CancellationTokenSource())
            {
                var child = ProcessCapture.RunAsync(powershell,
                    ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))],
                    runningCancellation.Token);
                var childPid = 0;
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (childPid == 0 && DateTime.UtcNow < deadline)
                    {
                        if (File.Exists(marker)) int.TryParse(File.ReadAllText(marker).Trim(), out childPid);
                        if (child.IsCompleted) await child.ConfigureAwait(false);
                        await Task.Delay(20).ConfigureAwait(false);
                    }
                    Check(childPid != 0, "isolated owned Doctor fixture reaches its running state");
                    runningCancellation.Cancel();
                    try { await child.ConfigureAwait(false); throw new Exception("Running Doctor ignored cancellation."); }
                    catch (OperationCanceledException) { }
                    deadline = DateTime.UtcNow.AddSeconds(3);
                    while (!Exited(childPid) && DateTime.UtcNow < deadline) await Task.Delay(20).ConfigureAwait(false);
                    Check(Exited(childPid), "canceling a running Doctor stops its owned child process");
                }
                finally
                {
                    runningCancellation.Cancel();
                    try { await child.ConfigureAwait(false); } catch (OperationCanceledException) { }
                }
            }
            File.Delete(marker);
            using var adapter = new GodotAdapter();
            var logs = new List<string>();
            adapter.LogLine += logs.Add;
            var gate = (SemaphoreSlim)typeof(GodotAdapter).GetField("_projectSwitch", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(adapter)!;
            await gate.WaitAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            Task[] queued;
            try
            {
                queued = [adapter.StartAsync(cancellation.Token), adapter.StopAsync(cancellation.Token),
                    adapter.RestartAsync(cancellation.Token), adapter.DoctorAsync(cancellation.Token),
                    adapter.RecoverAsync(false, cancellation.Token), adapter.RecoverAsync(true, cancellation.Token),
                    adapter.SelectProjectAsync(Path.Combine(fixture, "absent-project"), cancellation.Token)];
                Check(queued.All(task => !task.IsCompleted),
                    "Start, Stop, Restart, Doctor, both fallback modes and project selection wait for the same active Godot operation");
                cancellation.Cancel();
                foreach (var task in queued)
                {
                    try { await task.ConfigureAwait(false); throw new Exception("Queued operation ignored cancellation."); }
                    catch (OperationCanceledException) { }
                }
                Check(queued.All(task => task.IsCanceled) && logs.Count == 0 && !Directory.EnumerateFiles(fixture).Any(),
                    "canceling queued recovery performs no process command, project write or diagnostic action");
            }
            finally { gate.Release(); }
            await adapter.StopAsync().ConfigureAwait(false);
            Check(gate.CurrentCount == 1, "canceled waiters leave the shared operation gate usable by subsequent commands");

            logs.Clear();
            await ExpectMissingProjectAsync(adapter.RecoverAsync(true, CancellationToken.None)).ConfigureAwait(false);
            Check(logs.Count == 2 && logs[0].Contains("No Hub-owned tunnel to stop") && logs[1].Contains("No Godot project selected"),
                "ALT executes existing Stop then Doctor before Start validates the missing isolated project");
            logs.Clear();
            await ExpectMissingProjectAsync(adapter.RecoverAsync(false, CancellationToken.None)).ConfigureAwait(false);
            Check(logs.Count == 1 && logs[0].Contains("No Hub-owned tunnel to stop"),
                "Legacy restart reaches Start validation without running ALT Doctor");
            Check(!Directory.EnumerateFiles(fixture).Any(), "failed isolated recovery preserves settings and creates no project or profile files");
        }
        finally
        {
            foreach (var name in names) Environment.SetEnvironmentVariable(name, previous[name]);
            Directory.Delete(fixture, recursive: true);
        }
    }

    private static bool Exited(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }
    private static async Task ExpectMissingProjectAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); throw new Exception("Missing isolated project unexpectedly started."); }
        catch (InvalidDataException) { }
    }

    internal static void Ui(MainForm form)
    {
        var reconnect = Descendants<ReconnectButton>(form).Single(button => button.Name == "godot-reconnect");
        var header = (TableLayoutPanel)reconnect.Parent!;
        var start = header.Controls.OfType<Button>().Single(button => button.Text == "Start");
        var toggle = header.Controls.OfType<Button>().Single(button => button.Text is "⌄" or "⌃");
        Check(reconnect.Visible && reconnect.Enabled && reconnect.AccessibleName!.Contains("off"),
            "ReConnect is initially off and accessible from the visible Godot header");
        var initialToggle = toggle.Text;
        toggle.PerformClick();
        Application.DoEvents();
        Check(reconnect.Visible && start.Visible, "Godot card expansion keeps ReConnect and Start accessible");
        toggle.PerformClick();
        Application.DoEvents();
        Check(toggle.Text == initialToggle && reconnect.Visible && reconnect.Bottom <= header.ClientSize.Height &&
            start.Left - reconnect.Right >= (int)(36 * form.DeviceDpi / 96f),
            "collapsed Godot header retains ReConnect with approximately one centimetre before Start");

        using var off = Paint(reconnect, false, ReconnectState.Observing);
        using var green = Paint(reconnect, true, ReconnectState.Observing);
        using var red = Paint(reconnect, true, ReconnectState.Recovering);
        using var yellow = Paint(reconnect, true, ReconnectState.Waiting);
        Check(Different(off, green) && Different(green, red) && Different(red, yellow),
            "supplied RCicon artwork is absent when off and renders distinct green, red and yellow states");
        reconnect.SetState(false, ReconnectState.Observing, 0);
    }

    private static Bitmap Paint(ReconnectButton button, bool enabled, ReconnectState state)
    {
        button.SetState(enabled, state, state == ReconnectState.Waiting ? 9 : 0);
        var bitmap = new Bitmap(button.Width, button.Height);
        button.DrawToBitmap(bitmap, new Rectangle(Point.Empty, button.Size));
        return bitmap;
    }

    private static bool Different(Bitmap first, Bitmap second)
    {
        var differences = 0;
        for (var y = 0; y < first.Height; y++)
            for (var x = 0; x < first.Width; x++)
                if (first.GetPixel(x, y) != second.GetPixel(x, y)) differences++;
        return differences > 20;
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed) yield return typed;
            foreach (var match in Descendants<T>(child)) yield return match;
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: Godot recovery " + message);
        Console.WriteLine("PASS: Godot recovery " + message);
    }
}
