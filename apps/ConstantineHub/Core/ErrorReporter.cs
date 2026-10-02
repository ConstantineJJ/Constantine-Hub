namespace ConstantineHub.Core;

internal static class ErrorReporter
{
    internal static void Show(Exception error, string title, IWin32Window? owner = null)
    {
        string logNotice;
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ConstantineHub", "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"error-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
            File.WriteAllText(path, $"{DateTimeOffset.Now:O}\n{title}\n{error}");
            logNotice = "Details saved to:\n" + path;
        }
        catch (Exception logError)
        {
            logNotice = "Could not save error details: " + logError.Message;
        }

        MessageBox.Show(owner,
            $"{error.GetType().Name} (0x{error.HResult:X8})\n{error.Message}\n\n{logNotice}",
            title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
