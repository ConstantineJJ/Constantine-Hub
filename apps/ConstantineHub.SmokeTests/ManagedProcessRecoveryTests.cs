using System.ComponentModel;
using System.Diagnostics;
using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class ManagedProcessRecoveryTests
{
    internal static void Run()
    {
        foreach (var throws in new[] { false, true })
        {
            var rejectStop = true;
            using var managed = new ManagedProcess(_ => { }, process =>
            {
                if (rejectStop)
                {
                    if (throws) throw new Win32Exception("simulated stop denial");
                    return false;
                }
                process.Kill(entireProcessTree: true);
                return process.WaitForExit(3000);
            });
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!managed.Start(powershell, ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"]))
                throw new Exception("Failed to start isolated process fixture.");
            var pid = managed.ProcessId!.Value;
            using var actual = Process.GetProcessById(pid);
            try
            {
                var failed = false;
                try { managed.Stop(); }
                catch (InvalidOperationException) { failed = true; }
                Check(failed && managed.IsRunning && managed.ProcessId == pid && !actual.HasExited,
                    "unconfirmed Stop preserves ownership and reports failure: " + (throws ? "exception" : "timeout"));
                managed.Dispose();
                Check(managed.ProcessId == pid, "best-effort disposal retains failed ownership without interrupting other adapter cleanup");
                managed.Start(powershell, ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"]);
                Check(managed.ProcessId == pid, "failed Stop cannot create a duplicate owned process");
                rejectStop = false;
                managed.Stop();
                Check(actual.WaitForExit(3000) && managed.ProcessId is null,
                    "retrying Stop terminates the same retained process");
            }
            finally
            {
                rejectStop = false;
                if (!actual.HasExited) { actual.Kill(entireProcessTree: true); actual.WaitForExit(3000); }
            }
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
