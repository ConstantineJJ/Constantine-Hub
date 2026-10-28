using ConstantineHub.Adapters.LocalFiles;

namespace ConstantineHub.Core;

internal static class FileInbox
{
    internal static string Root => Path.GetFullPath(Environment.GetEnvironmentVariable("CONSTANTINE_HUB_INBOX")
        ?? @"F:\ChatGPT\Constantine Hub Files");

    internal static bool EnsureAgentAccess(string folder)
    {
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        var rules = File.Exists(LocalFilesSettingsStore.ConfigPath)
            ? LocalFilesSettingsStore.LoadRules().ToList() : [];
        if (rules.Any(r => r.Read && r.Write &&
            (string.Equals(r.Path, folder, StringComparison.OrdinalIgnoreCase) ||
             folder.StartsWith(r.Path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))) return false;
        var index = rules.FindIndex(r => string.Equals(r.Path, folder, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) rules[index] = rules[index] with { Read = true, Write = true };
        else rules.Add(new(folder, true, true, false));
        if (!File.Exists(LocalFilesSettingsStore.ConfigPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LocalFilesSettingsStore.ConfigPath)!);
            File.WriteAllText(LocalFilesSettingsStore.ConfigPath, "{\"schema_version\":1,\"allowed_roots\":[]}");
        }
        LocalFilesSettingsStore.SaveRules(rules);
        return true;
    }

    internal static async Task<IReadOnlyList<string>> ImportAsync(IEnumerable<string> files, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Root);
        var copied = new List<string>();
        foreach (var input in files)
        {
            var source = Path.GetFullPath(input);
            if (!File.Exists(source)) throw new FileNotFoundException("Choose files to add to the inbox.", source);
            var name = Path.GetFileName(source);
            if (string.Equals(Path.GetDirectoryName(source), Root, StringComparison.OrdinalIgnoreCase)) { copied.Add(source); continue; }
            var temp = Path.Combine(Root, ".import-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var reader = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                await using (var writer = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    await reader.CopyToAsync(writer, cancellationToken);
                var destination = Path.Combine(Root, name);
                for (var suffix = 1; ; suffix++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { File.Move(temp, destination, false); break; }
                    catch (IOException) when (File.Exists(destination))
                    { destination = Path.Combine(Root, $"{Path.GetFileNameWithoutExtension(name)} ({suffix}){Path.GetExtension(name)}"); }
                }
                copied.Add(destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return copied;
    }
}
