using System.Text;
using System.Text.RegularExpressions;

namespace ConstantineHub.Adapters.Godot;

internal sealed record GodotPluginStatus(
    bool Installed,
    bool Enabled,
    string SourceDirectory,
    string TargetDirectory,
    string Message);

internal static class GodotPluginInstaller
{
    private const string PluginPath = "res://addons/constantine_mcp/plugin.cfg";

    internal static GodotPluginStatus Inspect(GodotSettings settings)
    {
        var source = GodotSettingsStore.ProjectAddonSource;
        var target = Path.Combine(settings.ProjectRoot, "addons", "constantine_mcp");
        var installed = File.Exists(Path.Combine(target, "plugin.cfg")) &&
                        File.Exists(Path.Combine(target, "constantine_mcp_plugin.gd")) &&
                        File.Exists(Path.Combine(target, "editor_bridge.gd")) &&
                        File.Exists(Path.Combine(target, "runtime_bridge.gd"));
        var projectFile = Path.Combine(settings.ProjectRoot, "project.godot");
        var enabled = File.Exists(projectFile) && IsEnabled(File.ReadAllText(projectFile, Encoding.UTF8));
        var message = !Directory.Exists(source)
            ? "Hub Godot project-addon source is missing."
            : installed && enabled
                ? "Plugin installed and enabled."
                : installed
                    ? "Plugin installed but not enabled."
                    : "Plugin is not installed.";
        return new(installed, enabled, source, target, message);
    }

    internal static GodotPluginStatus InstallOrUpdate(GodotSettings settings)
    {
        var source = GodotSettingsStore.ProjectAddonSource;
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException("Godot project-addon source was not found: " + source);

        var target = Path.Combine(settings.ProjectRoot, "addons", "constantine_mcp");
        Directory.CreateDirectory(target);
        CopyTree(source, target);
        EnablePlugin(Path.Combine(settings.ProjectRoot, "project.godot"));
        return Inspect(settings);
    }

    private static void CopyTree(string source, string target)
    {
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static bool IsEnabled(string projectText)
    {
        var section = Regex.Match(projectText,
            @"(?ms)^\[editor_plugins\]\s*(?<body>.*?)(?=^\[|\z)");
        return section.Success && section.Groups["body"].Value.Contains(PluginPath, StringComparison.Ordinal);
    }

    private static void EnablePlugin(string projectFile)
    {
        if (!File.Exists(projectFile))
            throw new FileNotFoundException("project.godot was not found.", projectFile);

        var text = File.ReadAllText(projectFile, Encoding.UTF8);
        if (IsEnabled(text))
            return;

        var backup = projectFile + ".constantine-before-plugin.bak";
        if (!File.Exists(backup))
            File.Copy(projectFile, backup);

        var section = Regex.Match(text,
            @"(?ms)^\[editor_plugins\]\s*(?<body>.*?)(?=^\[|\z)");
        if (!section.Success)
        {
            if (!text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                text += Environment.NewLine;
            text += Environment.NewLine + "[editor_plugins]" + Environment.NewLine +
                    $"enabled=PackedStringArray(\"{PluginPath}\")" + Environment.NewLine;
        }
        else
        {
            var body = section.Groups["body"].Value;
            var enabledLine = Regex.Match(body, @"(?m)^enabled\s*=.*$");
            if (enabledLine.Success)
            {
                var current = enabledLine.Value;
                var paths = Regex.Matches(current, "\\\"(?<path>res://[^\\\"]+)\\\"")
                    .Cast<Match>()
                    .Select(match => match.Groups["path"].Value)
                    .ToList();
                if (!paths.Contains(PluginPath, StringComparer.Ordinal))
                    paths.Add(PluginPath);
                var replacement = "enabled=PackedStringArray(" +
                                  string.Join(", ", paths.Select(path => $"\"{path}\"")) + ")";
                body = body.Remove(enabledLine.Index, enabledLine.Length)
                           .Insert(enabledLine.Index, replacement);
            }
            else
            {
                body = body.TrimEnd() + Environment.NewLine +
                       $"enabled=PackedStringArray(\"{PluginPath}\")" + Environment.NewLine;
            }
            text = text.Remove(section.Groups["body"].Index, section.Groups["body"].Length)
                       .Insert(section.Groups["body"].Index, body);
        }

        var temp = projectFile + ".constantine.tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Replace(temp, projectFile, projectFile + ".constantine-last.bak", ignoreMetadataErrors: true);
    }
}
