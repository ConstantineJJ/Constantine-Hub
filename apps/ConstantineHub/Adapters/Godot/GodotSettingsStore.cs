using System.Text;
using System.Text.Json;

namespace ConstantineHub.Adapters.Godot;

internal sealed record GodotSettings(
    string ProjectRoot,
    string TunnelProfile,
    string TunnelId,
    int HealthPort,
    string EditorHost,
    int EditorPort,
    string RuntimeHost,
    int RuntimePort,
    string? GodotExecutable,
    string ToolsCRoot,
    bool AutoLaunchGodot);

internal static class GodotSettingsStore
{
    internal const string DefaultTunnelId = "tunnel_6aa32f76e3f881919f5757c792036950";

    internal static string ConfigPath
    {
        get
        {
            var explicitPath = Environment.GetEnvironmentVariable("CONSTANTINE_GODOT_MCP_CONFIG");
            if (!string.IsNullOrWhiteSpace(explicitPath))
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitPath));

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "ConstantineHub", "godot-mcp.json");
        }
    }

    internal static GodotSettings Load()
    {
        EnsureDefaultExists();
        var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
        var dto = JsonSerializer.Deserialize<GodotConfigDto>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Godot MCP config root is invalid.");

        var settings = new GodotSettings(
            ProjectRoot: NormalizeProject(dto.project_root ?? string.Empty),
            TunnelProfile: string.IsNullOrWhiteSpace(dto.tunnel_profile) ? "godot-local" : dto.tunnel_profile!,
            TunnelId: string.IsNullOrWhiteSpace(dto.tunnel_id) ? DefaultTunnelId : dto.tunnel_id!,
            HealthPort: dto.health_port is > 0 and <= 65535 ? dto.health_port.Value : 8082,
            EditorHost: string.IsNullOrWhiteSpace(dto.editor_host) ? "127.0.0.1" : dto.editor_host!,
            EditorPort: dto.editor_port is > 0 and <= 65535 ? dto.editor_port.Value : 6262,
            RuntimeHost: string.IsNullOrWhiteSpace(dto.runtime_host) ? "127.0.0.1" : dto.runtime_host!,
            RuntimePort: dto.runtime_port is > 0 and <= 65535 ? dto.runtime_port.Value : 6263,
            GodotExecutable: string.IsNullOrWhiteSpace(dto.godot_executable) ? null : Path.GetFullPath(Environment.ExpandEnvironmentVariables(dto.godot_executable!)),
            ToolsCRoot: NormalizeDirectory(dto.tools_c_root ?? @"E:\MyCreations\Tools_C", mustExist: false),
            AutoLaunchGodot: dto.auto_launch_godot ?? true);
        Validate(settings);
        return settings;
    }

    internal static void Save(GodotSettings settings)
    {
        Validate(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var dto = new GodotConfigDto
        {
            schema_version = 1,
            project_root = NormalizeProject(settings.ProjectRoot),
            tunnel_profile = settings.TunnelProfile,
            tunnel_id = settings.TunnelId,
            health_port = settings.HealthPort,
            editor_host = settings.EditorHost,
            editor_port = settings.EditorPort,
            runtime_host = settings.RuntimeHost,
            runtime_port = settings.RuntimePort,
            godot_executable = settings.GodotExecutable,
            tools_c_root = NormalizeDirectory(settings.ToolsCRoot, mustExist: false),
            max_file_bytes = 2_000_000,
            auto_launch_godot = settings.AutoLaunchGodot
        };
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, json + Environment.NewLine, new UTF8Encoding(false));
        if (File.Exists(ConfigPath))
            File.Replace(temp, ConfigPath, ConfigPath + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(temp, ConfigPath);
    }

    internal static void Validate(GodotSettings settings)
    {
        _ = NormalizeProject(settings.ProjectRoot);
        if (string.IsNullOrWhiteSpace(settings.TunnelProfile) || settings.TunnelProfile.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
            throw new InvalidDataException("Godot tunnel profile name is invalid.");
        if (!settings.TunnelId.StartsWith("tunnel_", StringComparison.Ordinal))
            throw new InvalidDataException("Godot tunnel id must begin with 'tunnel_'.");
        if (settings.HealthPort is < 1 or > 65535 || settings.EditorPort is < 1 or > 65535 || settings.RuntimePort is < 1 or > 65535)
            throw new InvalidDataException("Godot ports must be in range 1..65535.");
        if (!string.Equals(settings.EditorHost, "127.0.0.1", StringComparison.Ordinal) ||
            !string.Equals(settings.RuntimeHost, "127.0.0.1", StringComparison.Ordinal))
            throw new InvalidDataException("Godot bridges must be bound to 127.0.0.1 in this pass.");
    }

    internal static string RepositoryAdapterRoot
    {
        get
        {
            var current = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(current, "adapters", "godot-mcp")),
                Path.GetFullPath(Path.Combine(current, "..", "..", "..", "..", "adapters", "godot-mcp")),
                Path.GetFullPath(@"E:\MyCreations\Constantine-Hub\adapters\godot-mcp")
            };
            return candidates.FirstOrDefault(Directory.Exists) ?? candidates[^1];
        }
    }

    internal static string McpCommand => Path.Combine(RepositoryAdapterRoot, "run_godot_mcp.cmd");
    internal static string ProjectAddonSource => Path.Combine(RepositoryAdapterRoot, "project-addon", "addons", "constantine_mcp");

    private static void EnsureDefaultExists()
    {
        if (File.Exists(ConfigPath))
            return;
        var fallbackProject = @"F:\My Lab\my-lab-4-exp";
        if (!File.Exists(Path.Combine(fallbackProject, "project.godot")))
            return;
        Save(new GodotSettings(fallbackProject, "godot-local", DefaultTunnelId, 8082, "127.0.0.1", 6262, "127.0.0.1", 6263, null, @"E:\MyCreations\Tools_C", true));
    }

    private static string NormalizeProject(string path)
    {
        var full = NormalizeDirectory(path, mustExist: true);
        if (!File.Exists(Path.Combine(full, "project.godot")))
            throw new InvalidDataException($"Not a Godot project: {full}");
        return full;
    }

    private static string NormalizeDirectory(string path, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Directory path cannot be empty.");
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
        if (mustExist && !Directory.Exists(full))
            throw new DirectoryNotFoundException(full);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed class GodotConfigDto
    {
        public int schema_version { get; set; } = 1;
        public string? project_root { get; set; }
        public string? tunnel_profile { get; set; }
        public string? tunnel_id { get; set; }
        public int? health_port { get; set; }
        public string? editor_host { get; set; }
        public int? editor_port { get; set; }
        public string? runtime_host { get; set; }
        public int? runtime_port { get; set; }
        public string? godot_executable { get; set; }
        public string? tools_c_root { get; set; }
        public int max_file_bytes { get; set; } = 2_000_000;
        public bool? auto_launch_godot { get; set; }
    }
}
