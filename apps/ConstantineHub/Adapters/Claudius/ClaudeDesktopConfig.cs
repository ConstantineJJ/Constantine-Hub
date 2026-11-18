using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConstantineHub.Adapters.Claudius;

internal sealed record ClaudeRegistration(
    string? ConfigPath,
    bool Registered,
    bool Current,
    bool BlenderLabExtension,
    string Message);

/// <summary>
/// Owns exactly one entry, <c>mcpServers.blender-mcp-con</c>, in Claude Desktop's config.
/// Every other key is preserved; each write is preceded by a timestamped backup.
/// </summary>
internal static class ClaudeDesktopConfig
{
    internal const string ServerName = "blender-mcp-con";
    private const string FileName = "claude_desktop_config.json";
    private const string BlenderLabExtension = "ant.dir.gh.blender.blender-mcp";

    internal static string LauncherPath => Path.Combine(AppContext.BaseDirectory,
        "adapters", "claudius-channel", "run_claudius_channel.cmd");

    private static string CommandShell => Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } shell
        ? shell
        : Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>Most recently written Claude Desktop config (packaged or classic install).</summary>
    internal static string? Locate()
    {
        var overridePath = Environment.GetEnvironmentVariable("CONSTANTINE_CLAUDE_DESKTOP_CONFIG");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.GetFullPath(overridePath);

        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", FileName)
        };
        var packaged = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude-Data");
        try
        {
            if (Directory.Exists(packaged))
                candidates.AddRange(Directory.EnumerateDirectories(packaged).Select(d => Path.Combine(d, FileName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return candidates.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    internal static JsonObject BuildEntry(string launcher, string channelConfig) => new()
    {
        ["command"] = CommandShell,
        ["args"] = new JsonArray("/d", "/c", launcher, "--config", channelConfig),
        ["env"] = new JsonObject { ["PYTHONUNBUFFERED"] = "1", ["PYTHONIOENCODING"] = "utf-8" }
    };

    internal static ClaudeRegistration Inspect(string? configPath, string launcher, string channelConfig)
    {
        if (configPath is null || !File.Exists(configPath))
            return new(configPath, false, false, false, "Claude Desktop config not found. Open Claude Desktop once, then press Start.");

        var extension = Directory.Exists(Path.Combine(Path.GetDirectoryName(configPath)!, "Claude Extensions", BlenderLabExtension));
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject;
            var entry = root?["mcpServers"]?[ServerName];
            if (entry is null)
                return new(configPath, false, false, extension, "Not registered in Claude Desktop.");
            var current = JsonNode.DeepEquals(entry, BuildEntry(launcher, channelConfig));
            return new(configPath, true, current, extension, current
                ? "Registered in Claude Desktop."
                : "Registered entry points elsewhere; Start or Claude Setup will update it.");
        }
        catch (JsonException ex)
        {
            return new(configPath, false, false, extension, "Claude Desktop config is unreadable: " + ex.Message);
        }
    }

    /// <summary>Writes the gateway entry; returns the backup path, or null when already current.</summary>
    internal static string? Register(string configPath, string launcher, string channelConfig)
    {
        if (!File.Exists(launcher))
            throw new FileNotFoundException("Claudius gateway launcher is missing from this ConHub installation.", launcher);

        var text = File.ReadAllText(configPath);
        var root = JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidDataException("Claude Desktop config is not a JSON object.");
        var servers = root["mcpServers"] as JsonObject;
        if (servers is null)
        {
            if (root["mcpServers"] is not null)
                throw new InvalidDataException("Claude Desktop mcpServers is not a JSON object; refusing to replace it.");
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }

        var entry = BuildEntry(launcher, channelConfig);
        if (servers[ServerName] is { } existing && JsonNode.DeepEquals(existing, entry))
            return null;

        var backup = $"{configPath}.conhub-backup-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Copy(configPath, backup, overwrite: false);
        servers[ServerName] = entry;

        var temp = configPath + ".conhub.tmp";
        var json = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }) + Environment.NewLine;
        File.WriteAllText(temp, json);
        _ = JsonNode.Parse(File.ReadAllText(temp)); // validate before replacing the live file
        File.Move(temp, configPath, overwrite: true);
        return backup;
    }
}
