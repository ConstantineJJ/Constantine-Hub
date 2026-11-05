using System.Diagnostics;

namespace ConstantineHub.Core;

internal sealed class ManagedProcess : IDisposable
{
    private Process? _process;
    private readonly object _outputGate = new();
    private readonly Action<string> _log;
    private readonly Func<Process, bool> _stop;

    internal ManagedProcess(Action<string> log, Func<Process, bool>? stop = null)
    {
        _log = log;
        _stop = stop ?? (process =>
        {
            process.Kill(entireProcessTree: true);
            return process.WaitForExit(3000);
        });
    }

    internal bool IsRunning => _process is { HasExited: false };
    internal int? ProcessId => IsRunning ? _process!.Id : null;
    internal event Action? Started;

    internal bool Start(string executable, IEnumerable<string> arguments)
    {
        if (IsRunning)
            return true;

        try
        {
            if (_process is { HasExited: true })
            {
                _process.Dispose();
                _process = null;
            }

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                ForwardOutput(process, e.Data, "");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                ForwardOutput(process, e.Data, "ERR: ");
            };
            process.Exited += (_, _) =>
            {
                try { _log($"Process exited with code {process.ExitCode}."); }
                catch { }
            };

            if (!process.Start())
            {
                process.Dispose();
                return false;
            }

            lock (_outputGate)
            {
                _process = process;
                Started?.Invoke();
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _log($"Started PID {process.Id}: {executable} {string.Join(" ", psi.ArgumentList)}");
            return true;
        }
        catch (Exception ex)
        {
            _log("Failed to start process: " + ex.Message);
            // Attaching log readers can fail after process creation. Retain ownership.
            if (!IsRunning)
            {
                _process?.Dispose();
                lock (_outputGate) _process = null;
            }
            return false;
        }
    }

    internal void Stop()
    {
        var process = _process;
        if (process is null) return;

        try
        {
            var pid = process.Id;
            if (!process.HasExited && (!_stop(process) || !process.HasExited))
                throw new TimeoutException($"PID {pid} exit was not confirmed after Stop().");
            _log($"Stopped owned process PID {pid}.");
        }
        catch (Exception ex)
        {
            _log("Failed to stop owned process; ownership retained: " + ex.Message);
            throw new InvalidOperationException("Owned process stop was not confirmed; replacement was not started.", ex);
        }
        // Drain in-flight callbacks before the next generation resets readiness.
        lock (_outputGate) _process = null;
        process.Dispose();
    }

    private void ForwardOutput(Process process, string? line, string prefix)
    {
        lock (_outputGate)
        {
            if (ReferenceEquals(_process, process) && !string.IsNullOrWhiteSpace(line))
                _log(prefix + line);
        }
    }

    public void Dispose()
    {
        try { Stop(); }
        // Explicit Stop reports failure to its caller. Closing the application
        // must still attempt cleanup of the remaining independent adapters.
        catch (InvalidOperationException) { }
    }
}
