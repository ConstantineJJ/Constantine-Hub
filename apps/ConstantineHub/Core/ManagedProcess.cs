using System.Diagnostics;

namespace ConstantineHub.Core;

internal sealed class ManagedProcess : IDisposable
{
    private Process? _process;
    private readonly Action<string> _log;

    internal ManagedProcess(Action<string> log)
    {
        _log = log;
    }

    internal bool IsRunning => _process is { HasExited: false };
    internal int? ProcessId => IsRunning ? _process!.Id : null;

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
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _log(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _log("ERR: " + e.Data);
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

            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _log($"Started PID {process.Id}: {executable} {string.Join(" ", psi.ArgumentList)}");
            return true;
        }
        catch (Exception ex)
        {
            _log("Failed to start process: " + ex.Message);
            _process?.Dispose();
            _process = null;
            return false;
        }
    }

    internal void Stop()
    {
        if (!IsRunning)
            return;

        try
        {
            var pid = _process!.Id;
            _process.Kill(entireProcessTree: true);
            if (!_process.WaitForExit(3000))
                _log($"PID {pid} did not exit within 3 seconds after Kill().");
            else
                _log($"Stopped owned process PID {pid}.");
        }
        catch (Exception ex)
        {
            _log("Failed to stop owned process: " + ex.Message);
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    public void Dispose() => Stop();
}
