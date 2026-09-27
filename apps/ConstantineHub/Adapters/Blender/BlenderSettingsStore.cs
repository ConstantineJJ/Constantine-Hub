using System.Text.Json;

namespace ConstantineHub.Adapters.Blender;

internal sealed record BlenderSettings(
    string BlenderExecutable,
    bool AutoLaunchBlender,
    string BridgeHost,
    int BridgePort,
    string TunnelProfile)
{
    internal static BlenderSettings Default => new(
        BlenderExecutable: string.Empty,
        AutoLaunchBlender: true,
        BridgeHost: "127.0.0.1",
        BridgePort: 9876,
        TunnelProfile: "blender-local");
}

internal static class BlenderSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    internal static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ConstantineHub",
        "blender.json");

    internal static BlenderSettings Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var imported = TryImportLegacy();
            var initial = imported ?? BlenderSettings.Default;
            Save(initial);
            return initial;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<BlenderSettings>(
                File.ReadAllText(ConfigPath), JsonOptions) ?? BlenderSettings.Default;
            Validate(settings);
            return settings;
        }
        catch
        {
            // Never overwrite an unreadable user config silently. Return defaults so
            // the Hub can stay open and surface the problem through Doctor/status.
            return BlenderSettings.Default;
        }
    }

    internal static void Save(BlenderSettings settings)
    {
        Validate(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

        var temp = ConfigPath + ".tmp";
        var backup = ConfigPath + ".bak";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions) + Environment.NewLine);
        if (File.Exists(ConfigPath))
            File.Replace(temp, ConfigPath, backup, ignoreMetadataErrors: true);
        else
            File.Move(temp, ConfigPath);
    }

    internal static void Validate(BlenderSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BridgeHost))
            throw new InvalidDataException("Blender bridge host is empty.");
        if (settings.BridgePort is < 1 or > 65535)
            throw new InvalidDataException("Blender bridge port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(settings.TunnelProfile))
            throw new InvalidDataException("Blender tunnel profile is empty.");
        if (!string.IsNullOrWhiteSpace(settings.BlenderExecutable) &&
            !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(settings.BlenderExecutable)))
            throw new InvalidDataException("Configured Blender executable must be an absolute path.");
    }

    private static BlenderSettings? TryImportLegacy()
    {
        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlenderMCPCon",
            "config.json");
        if (!File.Exists(legacyPath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(legacyPath));
            var root = document.RootElement;

            string ReadString(string name, string fallback)
                => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? fallback
                    : fallback;

            bool ReadBool(string name, bool fallback)
                => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? value.GetBoolean()
                    : fallback;

            int ReadInt(string name, int fallback)
                => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
                    ? parsed
                    : fallback;

            var imported = new BlenderSettings(
                BlenderExecutable: ReadString("BlenderExecutable", string.Empty),
                AutoLaunchBlender: ReadBool("AutoLaunchBlender", true),
                BridgeHost: ReadString("BlenderHost", "127.0.0.1"),
                BridgePort: ReadInt("BlenderPort", 9876),
                TunnelProfile: ReadString("TunnelProfile", "blender-local"));
            Validate(imported);
            return imported;
        }
        catch
        {
            return null;
        }
    }
}
