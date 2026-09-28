namespace ConstantineHub;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var options = ParseArgs(args);
        var form = new MainForm();
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
                    "Update rolled back",
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
