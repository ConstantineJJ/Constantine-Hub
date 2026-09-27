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

    private static bool IsExecutableFile(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
}
