using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConstantineHub.Adapters.Claudius;

/// <summary>ConHub-owned switch read by the Claudius gateway before every tool call.</summary>
internal sealed record ClaudiusSettings(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("server_command")] string ServerCommand)
{
    internal static ClaudiusSettings Default => new(1, false, string.Empty);
}

/// <summary>Heartbeat published by the gateway process that Claude Desktop owns.</summary>
internal sealed record ClaudiusGatewayState(
    [property: JsonPropertyName("gateway_pid")] int GatewayPid,
    [property: JsonPropertyName("child_pid")] int? ChildPid,
    [property: JsonPropertyName("started_utc")] DateTimeOffset? StartedUtc,
    [property: JsonPropertyName("heartbeat_utc")] DateTimeOffset? HeartbeatUtc,
    [property: JsonPropertyName("stopped_utc")] DateTimeOffset? StoppedUtc,
    [property: JsonPropertyName("client")] string? Client,
    [property: JsonPropertyName("calls")] int Calls,
    [property: JsonPropertyName("blocked")] int Blocked,
    [property: JsonPropertyName("last_call_utc")] DateTimeOffset? LastCallUtc,
    [property: JsonPropertyName("last_tool")] string? LastTool);

internal static class ClaudiusChannelStore
{
    internal static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string ConfigPath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("CONSTANTINE_CLAUDIUS_CONFIG");
            return !string.IsNullOrWhiteSpace(overridePath)
                ? Path.GetFullPath(overridePath)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ConstantineHub", "claudius.json");
        }
    }

    internal static string StatePath => Path.Combine(Path.GetDirectoryName(ConfigPath)!, "claudius-state.json");

    /// <summary>Missing config means a closed channel; an unreadable one is reported, never overwritten.</summary>
    internal static ClaudiusSettings Load()
    {
        if (!File.Exists(ConfigPath))
            return ClaudiusSettings.Default;
        try
        {
            return JsonSerializer.Deserialize<ClaudiusSettings>(File.ReadAllText(ConfigPath), JsonOptions)
                ?? throw new InvalidDataException("Claudius channel config is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Claudius channel config is unreadable: " + ex.Message, ex);
        }
    }

    internal static void Save(ClaudiusSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings with { SchemaVersion = 1 }, JsonOptions) + Environment.NewLine);
        if (File.Exists(ConfigPath))
            File.Replace(temp, ConfigPath, ConfigPath + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(temp, ConfigPath);
    }

    internal static ClaudiusGatewayState? LoadState()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<ClaudiusGatewayState>(File.ReadAllText(StatePath), JsonOptions)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A gateway is live only with a fresh heartbeat, no stop marker and a running process.</summary>
    internal static bool IsLive(ClaudiusGatewayState? state, DateTimeOffset now)
    {
        if (state is null || state.StoppedUtc is not null || state.HeartbeatUtc is null)
            return false;
        if (now - state.HeartbeatUtc.Value > HeartbeatTimeout)
            return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(state.GatewayPid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
