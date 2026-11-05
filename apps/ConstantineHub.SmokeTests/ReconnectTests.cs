using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class ReconnectTests
{
    internal static void Run()
    {
        ManagedProcessRecoveryTests.Run();
        RunAsync().GetAwaiter().GetResult();
    }

    private static async Task RunAsync()
    {
        var calls = new List<bool>();
        var delays = new List<TimeSpan>();
        using var fallback = new ReconnectFallback(_ => Task.FromResult(false),
            (alt, _) => { calls.Add(alt); return Task.CompletedTask; },
            (delay, _) => { delays.Add(delay); return Task.CompletedTask; });
        await fallback.CheckAsync();
        Check(calls.Count == 0, "off performs no automatic action");
        fallback.Enable();
        await fallback.CheckAsync();
        Check(calls.SequenceEqual(new[] { false, false, true, false, false, true, false, false, true }), "three cycles of two Legacy and one ALT attempt");
        Check(fallback.State == ReconnectState.Waiting && fallback.Attempts == 9, "exhaustion waits in yellow after exactly nine attempts");
        await fallback.CheckAsync();
        Check(calls.Count == 9 && delays.All(d => d.TotalSeconds is >= 5 and <= 30), "yellow never loops and retry intervals stay bounded");
        fallback.Enable();
        await fallback.CheckAsync();
        Check(calls.Count == 18, "manual yellow rearm permits one new series");

        var probes = 0;
        calls.Clear();
        using var success = new ReconnectFallback(_ => Task.FromResult(++probes >= 4),
            (alt, _) => { calls.Add(alt); return Task.CompletedTask; }, (_, _) => Task.CompletedTask);
        success.Enable();
        await success.CheckAsync();
        Check(calls.Count == 2 && success.Attempts == 0 && success.State == ReconnectState.Observing,
            "success immediately stops recovery and clears attempts");
        probes = 0;
        using var transient = new ReconnectFallback(_ => Task.FromResult(++probes > 1),
            (_, _) => throw new Exception("transient failure must not restart"), (_, _) => Task.CompletedTask);
        transient.Enable(); await transient.CheckAsync();
        Check(transient.State == ReconnectState.Observing, "ambiguous failure is confirmed by another probe");

        using var healthy = new ReconnectFallback(_ => Task.FromResult(true),
            (_, _) => throw new Exception("healthy connection must not restart"), (_, _) => Task.CompletedTask);
        healthy.ObserveLog("write |1: file already closed"); // old/off-session event
        healthy.Enable(); healthy.ObserveLog("failed to post response to control plane");
        await healthy.CheckAsync();
        Check(healthy.Attempts == 0, "fresh critical log cannot override healthy status; old events ignored");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        calls.Clear();
        using var cancelled = new ReconnectFallback(_ => Task.FromResult(false),
            async (alt, token) => { calls.Add(alt); entered.SetResult(); await Task.Delay(Timeout.Infinite, token); },
            (_, _) => Task.CompletedTask);
        cancelled.Enable(); var running = cancelled.CheckAsync(); await entered.Task;
        await cancelled.CheckAsync();
        cancelled.Disable(); await running;
        Check(calls.Count == 1 && !cancelled.Enabled && cancelled.Attempts == 0,
            "concurrent checks serialize and disabling cancels the active operation");
        Check(!ReconnectFallback.IsCriticalConnectionLog("MCP tool failed: invalid argument -32603") &&
            !ReconnectFallback.IsCriticalConnectionLog("Node not found: MortarAmmoPickups") &&
            ReconnectFallback.IsCriticalConnectionLog("read |0: file already closed") &&
            ReconnectFallback.IsCriticalConnectionLog("stdio MCP command exited; tunnel-client remains running in degraded mode"),
            "ordinary tool errors do not signal connection failure");

        probes = 0;
        var messages = new List<string>();
        using var diagnosed = new ReconnectFallback(_ => Task.FromResult(++probes >= 3),
            (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask,
            () => "Runtime bridge: timed out; MCP readiness inferred, not an MCP roundtrip.");
        diagnosed.LogLine += messages.Add;
        diagnosed.Enable();
        await diagnosed.CheckAsync();
        Check(diagnosed.State == ReconnectState.Observing && messages.Count(m => m.Contains("restored")) == 1 &&
            messages.Last().Contains("tunnel/editor") && messages.Last().Contains("Runtime bridge: timed out"),
            "restoration logs exactly once and identifies the checks and separate runtime failure");

        await DisablingScheduledWaitsAsync();

        var isConnected = true;
        var manualStopRecoveries = 0;
        using var armed = new ReconnectFallback(_ => Task.FromResult(isConnected),
            (_, _) => { manualStopRecoveries++; isConnected = true; return Task.CompletedTask; },
            (_, _) => Task.CompletedTask);
        armed.Enable();
        await armed.CheckAsync();
        isConnected = false; // Intentional Stop does not disarm an enabled fallback.
        await armed.CheckAsync();
        Check(armed.Enabled && isConnected && manualStopRecoveries == 1,
            "armed ReConnect restores a manually stopped connection until explicitly disabled");
        armed.Disable();
        isConnected = false;
        for (var tick = 0; tick < 3; tick++) await armed.CheckAsync();
        Check(!armed.Enabled && manualStopRecoveries == 1,
            "disabled ReConnect never rearms itself on subsequent timer checks");
    }

    private static async Task DisablingScheduledWaitsAsync()
    {
        foreach (var seconds in new[] { 5, 10 })
        {
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var recoveries = 0;
            var checks = 0;
            var logs = new List<string>();
            using var fallback = new ReconnectFallback(_ => { checks++; return Task.FromResult(false); },
                (_, _) => { recoveries++; return Task.CompletedTask; },
                (duration, _) =>
                {
                    if (duration.TotalSeconds != seconds) return Task.CompletedTask;
                    waiting.SetResult();
                    // Deliberately ignore cancellation to exercise a late scheduled
                    // continuation, not just Task.Delay's cooperative cancellation.
                    return release.Task;
                });
            fallback.LogLine += logs.Add;
            fallback.Enable();
            var active = fallback.CheckAsync();
            await waiting.Task;
            fallback.Disable();
            var beforeDisable = (recoveries, checks, logs.Count);
            release.SetResult();
            await active;
            for (var tick = 0; tick < 3; tick++) await fallback.CheckAsync();
            Check(!fallback.Enabled && fallback.Attempts == 0 && fallback.State == ReconnectState.Observing &&
                (recoveries, checks, logs.Count) == beforeDisable && recoveries == (seconds == 5 ? 0 : 1),
                $"manual disable cancels the pending {seconds}s wait; late continuation cannot probe, retry, log restoration or rearm");
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: ReConnect " + message);
        Console.WriteLine("PASS: ReConnect " + message);
    }
}
