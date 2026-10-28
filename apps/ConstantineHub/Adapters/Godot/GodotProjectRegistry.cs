using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConstantineHub.Adapters.Godot;

internal sealed record GodotProjectEntry(string Name, string ProjectRoot, bool Exists)
{
    public override string ToString() => $"{Name}{(Exists ? "" : " (missing)")} — {ProjectRoot}";
}

internal static class GodotProjectRegistry
{
    internal static string RegistryPath => Path.Combine(Path.GetDirectoryName(GodotSettingsStore.ConfigPath)!, "godot-projects.json");

    internal static IReadOnlyList<GodotProjectEntry> Load()
    {
        var paths = File.Exists(RegistryPath)
            ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RegistryPath)) ?? [] : [];
        if (File.Exists(GodotSettingsStore.ConfigPath))
        {
            using var config = JsonDocument.Parse(File.ReadAllText(GodotSettingsStore.ConfigPath));
            if (config.RootElement.TryGetProperty("project_root", out var current) && current.GetString() is { Length: > 0 } root)
                paths.Insert(0, root);
        }
        return paths.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).Select(Describe).ToArray();
    }

    internal static GodotProjectEntry Register(string folderOrProjectFile)
    {
        var path = NormalizePath(folderOrProjectFile);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        if (!File.Exists(Path.Combine(path, "project.godot")))
            throw new InvalidDataException($"Choose a Godot project folder containing project.godot: {path}");
        var paths = Load().Select(e => e.ProjectRoot).Append(path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
        var temporary = RegistryPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        File.Move(temporary, RegistryPath, overwrite: true);
        return Describe(path);
    }

    internal static bool SameProject(string left, string right)
        => string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
        if (Path.GetFileName(full).Equals("project.godot", StringComparison.OrdinalIgnoreCase)) full = Path.GetDirectoryName(full)!;
        return Path.TrimEndingDirectorySeparator(full);
    }

    private static GodotProjectEntry Describe(string path)
    {
        var file = Path.Combine(path, "project.godot");
        var name = Path.GetFileName(path);
        if (File.Exists(file))
        {
            foreach (var line in File.ReadLines(file))
            {
                var match = Regex.Match(line, "^\\s*config/name\\s*=\\s*(\"(?:[^\"\\\\]|\\\\.)*\")\\s*$");
                if (!match.Success) continue;
                try { name = JsonSerializer.Deserialize<string>(match.Groups[1].Value) ?? name; }
                catch (JsonException) { }
                break;
            }
        }
        return new(name, path, File.Exists(file));
    }
}
