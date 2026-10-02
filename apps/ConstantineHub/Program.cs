namespace ConstantineHub;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Core.ErrorReporter.Show(e.Exception, "Constantine Hub — unexpected error");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Core.ErrorReporter.Show(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()),
                "Constantine Hub — fatal error");
        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            Core.ErrorReporter.Show(ex, "Constantine Hub — startup or application error");
            Environment.ExitCode = 1;
        }
    }

    private static void Run(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var options = ParseArgs(args);
        var form = new MainForm();
        UiPass.Apply(form);
        UiAcceptancePass.Apply(form);
        form.Shown += (_, _) =>
        {
            if (options.TryGetValue("update-ok-file", out var marker) && !string.IsNullOrWhiteSpace(marker))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(marker))!);
                    File.WriteAllText(marker, DateTimeOffset.Now.ToString("O"));
                }
                catch
                {
                    // The updater will time out and rollback if the startup marker cannot be written.
                }
            }

            if (options.TryGetValue("updated-from", out var from) &&
                options.TryGetValue("updated-to", out var to))
            {
                MessageBox.Show(
                    form,
                    $"Constantine Hub updated successfully: {from} → {to}",
                    "Update complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            if (options.TryGetValue("rollback-notice", out var rollback))
            {
                MessageBox.Show(
                    form,
                    rollback,
                    "Update not installed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        Application.Run(form);
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
}
