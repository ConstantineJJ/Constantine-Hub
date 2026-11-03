using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class ReconnectTests
{
    internal static void Run() => RunAsync().GetAwaiter().GetResult();

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
            ReconnectFallback.IsCriticalConnectionLog("read |0: file already closed"),
            "ordinary tool errors do not signal connection failure");
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: ReConnect " + message);
        Console.WriteLine("PASS: ReConnect " + message);
    }
}
