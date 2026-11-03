using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;

namespace ConstantineHub.Core;

internal sealed record TunnelProfile(
    string Name,
    string ProfilePath,
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
        // Read one leaf line only. Native tunnel-client emits JSON-compatible quoted
        // YAML strings, including escaped quotes around executable paths with spaces.
        var match = Regex.Match(yaml, @"(?m)^[ \t]*" + Regex.Escape(key) + @"[ \t]*:[ \t]*(?<value>[^\r\n]*)\r?$");
        if (!match.Success) return null;
        var scalar = match.Groups["value"].Value.Trim();
        if (scalar.Length == 0 || scalar[0] == '#') return null;
        if (scalar[0] == '"')
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(scalar);
                var reader = new Utf8JsonReader(bytes);
                if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                    throw new JsonException("Expected a quoted string.");
                var value = reader.GetString();
                ValidateTail(Encoding.UTF8.GetString(bytes.AsSpan((int)reader.BytesConsumed)), key);
                return value;
            }
            catch (JsonException ex) { throw new InvalidDataException($"Tunnel profile {key} contains an invalid quoted scalar.", ex); }
        }
        if (scalar[0] == '\'')
        {
            var value = new StringBuilder();
            for (var i = 1; i < scalar.Length; i++)
            {
                if (scalar[i] != '\'') { value.Append(scalar[i]); continue; }
                if (i + 1 < scalar.Length && scalar[i + 1] == '\'') { value.Append('\''); i++; continue; }
                ValidateTail(scalar[(i + 1)..], key);
                return value.ToString();
            }
            throw new InvalidDataException($"Tunnel profile {key} contains an unterminated quoted scalar.");
        }
        for (var i = 0; i < scalar.Length; i++)
            if (scalar[i] == '#' && (i == 0 || char.IsWhiteSpace(scalar[i - 1]))) return scalar[..i].TrimEnd();
        return scalar;
    }

    private static void ValidateTail(string tail, string key)
    {
        if (tail.Length == 0 || string.IsNullOrWhiteSpace(tail) ||
            (char.IsWhiteSpace(tail[0]) && tail.TrimStart().StartsWith('#'))) return;
        throw new InvalidDataException($"Tunnel profile {key} contains unexpected text after its quoted scalar.");
    }

    internal static string CommandPath(string command)
    {
        var path = Environment.ExpandEnvironmentVariables(command.Trim());
        if (path.StartsWith('"'))
        {
            if (path.Length < 2 || !path.EndsWith('"') || path[1..^1].Contains('"'))
                throw new InvalidDataException("The MCP command must contain one executable path without arguments.");
            path = path[1..^1];
        }
        if (path.Contains('"') || path.Any(char.IsControl))
            throw new InvalidDataException("The MCP command contains invalid executable path characters.");
        return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
    }
}
