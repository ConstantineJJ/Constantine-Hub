using Microsoft.Win32;

namespace ConstantineHub.Core;

internal static class ExecutableLocator
{
    internal static string? FindTunnelClient()
    {
        var explicitPath = Environment.GetEnvironmentVariable("CONSTANTINE_TUNNEL_CLIENT");
        if (IsExecutableFile(explicitPath))
            return Path.GetFullPath(explicitPath!);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var known = Path.Combine(userProfile, "chatgpt-blender-mcp", "tunnel-client.exe");
        if (File.Exists(known))
            return known;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(segment, "tunnel-client.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries and continue discovery.
            }
        }

        return null;
    }

    internal static string? FindBlender(string? configured = null)
    {
        if (IsExecutableFile(configured))
            return Path.GetFullPath(configured!);

        var candidates = new List<string>();
        var steamPath = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            "SteamPath",
            null) as string;
        if (!string.IsNullOrWhiteSpace(steamPath))
            candidates.Add(Path.Combine(
                steamPath.Replace('/', Path.DirectorySeparatorChar),
                "steamapps", "common", "Blender", "blender.exe"));

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        candidates.Add(Path.Combine(pf, "Steam", "steamapps", "common", "Blender", "blender.exe"));
        candidates.Add(Path.Combine(pfx86, "Steam", "steamapps", "common", "Blender", "blender.exe"));

        var foundation = Path.Combine(pf, "Blender Foundation");
        if (Directory.Exists(foundation))
        {
            candidates.AddRange(
                Directory.GetDirectories(foundation, "Blender *")
                    .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(x => Path.Combine(x, "blender.exe")));
        }

        var found = candidates.FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(found))
            return Path.GetFullPath(found);

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(segment.Trim('"'), "blender.exe");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch { }
        }

        return null;
    }

    private static bool IsExecutableFile(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
}
