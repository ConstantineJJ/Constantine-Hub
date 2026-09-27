using System.Text.RegularExpressions;

namespace ConstantineHub.Core;

internal sealed record TunnelProfile(
    string Name,
    string Path,
    string TunnelId,
    string McpCommand,
    string ListenAddress,
    int HealthPort)
{
    internal static TunnelProfile Load(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) ||
            !Regex.IsMatch(profileName, "^[A-Za-z0-9_-]+$"))
            throw new InvalidDataException("Tunnel profile name is invalid.");

        var profileDir = Environment.GetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_DIR");
        if (string.IsNullOrWhiteSpace(profileDir))
            profileDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "tunnel-client");

        var explicitFile = Environment.GetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_FILE");
        var profilePath = string.IsNullOrWhiteSpace(explicitFile)
            ? Path.Combine(profileDir, profileName + ".yaml")
            : explicitFile;

        if (!File.Exists(profilePath))
            throw new FileNotFoundException("Tunnel profile not found.", profilePath);

        var yaml = File.ReadAllText(profilePath);
        var tunnelId = ReadScalar(yaml, "tunnel_id");
        var command = ReadScalar(yaml, "command");
        var address = ReadScalar(yaml, "listen_addr");
        if (string.IsNullOrWhiteSpace(tunnelId) ||
            string.IsNullOrWhiteSpace(command) ||
            string.IsNullOrWhiteSpace(address))
            throw new InvalidDataException("Tunnel profile is missing tunnel_id, command, or listen_addr.");

        var parts = address.Split(':');
        if (parts.Length != 2 ||
            !string.Equals(parts[0], "127.0.0.1", StringComparison.Ordinal) ||
            !int.TryParse(parts[1], out var port) || port is < 1 or > 65535)
            throw new InvalidDataException("Tunnel profile must use a fixed 127.0.0.1 health listener.");

        return new TunnelProfile(
            profileName,
            Path.GetFullPath(profilePath),
            tunnelId,
            command,
            address,
            port);
    }

    private static string? ReadScalar(string yaml, string key)
    {
        var match = Regex.Match(
            yaml,
            @"(?m)^\s*" + Regex.Escape(key) + @"\s*:\s*['"" ]?(?<value>[^'""\r\n#]+)");
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }
}
