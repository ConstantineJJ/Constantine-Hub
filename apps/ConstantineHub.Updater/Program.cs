using System.Diagnostics;

namespace ConstantineHub.Updater;

internal static class Program
{
    private const int StartupTimeoutSeconds = 20;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            var source = Required(options, "source");
            var target = Required(options, "target");
            var restartName = Required(options, "restart");
            var fromVersion = options.GetValueOrDefault("from", "unknown");
            var toVersion = options.GetValueOrDefault("to", "unknown");
            var pid = int.Parse(Required(options, "pid"));

            source = Path.GetFullPath(source);
            target = Path.GetFullPath(target);
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException("Update payload was not found: " + source);
            if (!Directory.Exists(target))
                throw new DirectoryNotFoundException("Hub install directory was not found: " + target);

            var stateRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ConstantineHub",
                "updates");
            Directory.CreateDirectory(stateRoot);
            var logPath = Path.Combine(stateRoot, "updater.log");
            Log(logPath, $"Starting update {fromVersion} -> {toVersion}. Source={source}; Target={target}");

            WaitForProcessExit(pid, logPath);

            var backupRoot = Path.Combine(stateRoot, "backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupRoot);

            var replaced = new List<string>();
            var created = new List<string>();
            try
            {
                foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(source, sourceFile);
                    var targetFile = Path.GetFullPath(Path.Combine(target, relative));
                    EnsureInside(target, targetFile);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

                    if (File.Exists(targetFile))
                    {
                        var backupFile = Path.Combine(backupRoot, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                        File.Copy(targetFile, backupFile, overwrite: true);
                        replaced.Add(relative);
                    }
                    else
                    {
                        created.Add(relative);
                    }

                    File.Copy(sourceFile, targetFile, overwrite: true);
                }

                var restartPath = Path.Combine(target, restartName);
                if (!File.Exists(restartPath))
                    throw new FileNotFoundException("Updated Hub executable was not found.", restartPath);

                var marker = Path.Combine(stateRoot, "startup-ok-" + Guid.NewGuid().ToString("N") + ".txt");
                var startInfo = new ProcessStartInfo
                {
                    FileName = restartPath,
                    WorkingDirectory = target,
                    UseShellExecute = true
                };
                startInfo.ArgumentList.Add("--update-ok-file");
                startInfo.ArgumentList.Add(marker);
                startInfo.ArgumentList.Add("--updated-from");
                startInfo.ArgumentList.Add(fromVersion);
                startInfo.ArgumentList.Add("--updated-to");
                startInfo.ArgumentList.Add(toVersion);

                var newHub = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not restart Constantine Hub after update.");
                Log(logPath, $"Started updated Hub PID {newHub.Id}; waiting for startup marker.");

                if (WaitForMarker(marker, TimeSpan.FromSeconds(StartupTimeoutSeconds)))
                {
                    Log(logPath, "Updated Hub reported successful startup. Update complete.");
                    TryDelete(marker);
                    return 0;
                }

                Log(logPath, "Updated Hub did not report startup success in time. Rolling back.");
                try
                {
                    if (!newHub.HasExited)
                        newHub.Kill(entireProcessTree: true);
                }
                catch { }

                RestoreBackup(target, backupRoot, replaced, created, logPath);
                RestartRollback(target, restartName, fromVersion, toVersion, logPath);
                return 2;
            }
            catch (Exception ex)
            {
                Log(logPath, "Update failed: " + ex);
                RestoreBackup(target, backupRoot, replaced, created, logPath);
                RestartRollback(target, restartName, fromVersion, toVersion, logPath);
                return 1;
            }
        }
        catch (Exception ex)
        {
            try
            {
                var stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConstantineHub", "updates");
                Directory.CreateDirectory(stateRoot);
                Log(Path.Combine(stateRoot, "updater.log"), "Fatal updater error: " + ex);
            }
            catch { }
            return 1;
        }
    }

    private static void WaitForProcessExit(int pid, string logPath)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            Log(logPath, $"Waiting for Hub PID {pid} to exit.");
            if (!process.WaitForExit(30000))
                throw new TimeoutException("The running Constantine Hub did not exit within 30 seconds.");
        }
        catch (ArgumentException)
        {
            // Process already exited.
        }
    }

    private static bool WaitForMarker(string marker, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(marker))
                return true;
            Thread.Sleep(250);
        }
        return false;
    }

    private static void RestoreBackup(string target, string backupRoot, IEnumerable<string> replaced, IEnumerable<string> created, string logPath)
    {
        try
        {
            foreach (var relative in created.Reverse())
            {
                var path = Path.Combine(target, relative);
                if (File.Exists(path))
                    File.Delete(path);
            }

            foreach (var relative in replaced.Reverse())
            {
                var backupFile = Path.Combine(backupRoot, relative);
                var targetFile = Path.Combine(target, relative);
                if (!File.Exists(backupFile))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                File.Copy(backupFile, targetFile, overwrite: true);
            }
            Log(logPath, "Rollback restored the previous installation files.");
        }
        catch (Exception ex)
        {
            Log(logPath, "Rollback encountered an error: " + ex);
        }
    }

    private static void RestartRollback(string target, string restartName, string fromVersion, string toVersion, string logPath)
    {
        try
        {
            var restartPath = Path.Combine(target, restartName);
            if (!File.Exists(restartPath))
                return;
            var startInfo = new ProcessStartInfo
            {
                FileName = restartPath,
                WorkingDirectory = target,
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("--rollback-notice");
            startInfo.ArgumentList.Add($"Update {fromVersion} -> {toVersion} failed and was rolled back.");
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Log(logPath, "Could not restart rolled-back Hub: " + ex);
        }
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue;
            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "true";
            result[key] = value;
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required --{key} argument.");

    private static void EnsureInside(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update payload attempted to escape the install directory.");
    }

    private static void Log(string path, string message)
    {
        File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
