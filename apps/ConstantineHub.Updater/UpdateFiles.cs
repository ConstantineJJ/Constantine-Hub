using System.Diagnostics;

namespace ConstantineHub.Updater;

internal static class UpdateFiles
{
    internal static void WaitUntilWritable(IEnumerable<string> files, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        foreach (var path in files.Where(File.Exists))
            Retry(() =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }, path, timeout - timer.Elapsed);
    }

    internal static void Replace(string source, string target, string? backup, TimeSpan timeout)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var pending = target + ".hub-update-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, pending);
            if (backup is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup, overwrite: true);
            }
            // Never truncate a live file. A failed rename leaves its original bytes intact.
            Retry(() => File.Move(pending, target, overwrite: true), target, timeout);
        }
        finally
        {
            try { if (File.Exists(pending)) File.Delete(pending); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void Retry(Action action, string path, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { action(); return; }
            catch (Exception ex) when (ex is IOException && (ex.HResult & 0xffff) is 32 or 33 or 1224
                || ex is UnauthorizedAccessException && (ex.HResult & 0xffff) == 5)
            {
                if (timer.Elapsed >= timeout)
                    throw new IOException($"File cannot be replaced: {path}. Close other Constantine Hub instances or the program using this file, and check write permissions, then retry.", ex);
                Thread.Sleep(100);
            }
        }
    }
}
