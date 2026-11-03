namespace ConstantineHub.Core;

internal enum ReconnectState { Observing, Recovering, Waiting }

// Only coordinates the existing adapter commands. It never reads user requests
// or historical log files, creates processes, or repairs project contents.
internal sealed class ReconnectFallback(
    Func<CancellationToken, Task<bool>> connected,
    Func<bool, CancellationToken, Task> recover,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IDisposable
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private CancellationTokenSource? _session;
    private int _running;
    private int _critical;
    internal bool Enabled => _session is not null;
    internal int Attempts { get; private set; }
    internal ReconnectState State { get; private set; }
    internal event Action? Changed;
    internal event Action<string>? LogLine;

    internal void Enable()
    {
        Disable();
        _session = new CancellationTokenSource();
        State = ReconnectState.Observing;
        Changed?.Invoke();
    }

    internal void Disable()
    {
        var old = _session;
        _session = null;
        old?.Cancel();
        old?.Dispose();
        Attempts = 0;
        Interlocked.Exchange(ref _critical, 0);
        State = ReconnectState.Observing;
        Changed?.Invoke();
    }

    internal static bool IsCriticalConnectionLog(string line) =>
        new[] { "file already closed", "MCP server exited", "Received response without valid ID",
            "failed to post response to control plane", "connection reset", "broken pipe" }
        .Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase));

    internal void ObserveLog(string line)
    {
        if (Enabled && IsCriticalConnectionLog(line)) Interlocked.Exchange(ref _critical, 1);
    }

    internal async Task CheckAsync()
    {
        if (_session is null || State == ReconnectState.Waiting || Interlocked.Exchange(ref _running, 1) != 0) return;
        var token = _session.Token;
        try
        {
            if (await ProbeAsync(token)) return;
            var critical = Interlocked.Exchange(ref _critical, 0) != 0;
            State = ReconnectState.Recovering;
            Changed?.Invoke();
            LogLine?.Invoke(critical ? "New critical connection event; confirming Godot failure." : "Godot status unavailable; confirming failure.");
            await _delay(TimeSpan.FromSeconds(5), token);
            if (await ProbeAsync(token)) { Restored(); return; }
            for (var attempt = 1; attempt <= 9; attempt++)
            {
                token.ThrowIfCancellationRequested();
                Attempts = attempt;
                Changed?.Invoke();
                var alt = attempt % 3 == 0;
                LogLine?.Invoke($"Attempt {attempt}/9, cycle {(attempt - 1) / 3 + 1}: {(alt ? "ALT Stop → Doctor → Start" : "Legacy Restart")}.");
                try { await recover(alt, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { LogLine?.Invoke("Attempt failed: " + ex.Message); }
                token.ThrowIfCancellationRequested();
                if (await ProbeAsync(token)) { Restored(); return; }
                if (attempt < 9) await _delay(TimeSpan.FromSeconds(10), token);
            }
            State = ReconnectState.Waiting;
            LogLine?.Invoke("Nine attempts exhausted. Click ReConnect to rearm.");
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    private async Task<bool> ProbeAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var result = await connected(token);
            token.ThrowIfCancellationRequested();
            if (result) Interlocked.Exchange(ref _critical, 0);
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { LogLine?.Invoke("Status check failed: " + ex.Message); return false; }
    }

    private void Restored()
    {
        Attempts = 0;
        State = ReconnectState.Observing;
        LogLine?.Invoke("Godot connection restored; monitoring.");
        Changed?.Invoke();
    }

    public void Dispose() => Disable();
}
