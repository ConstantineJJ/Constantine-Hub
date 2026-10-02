using System.Diagnostics;
using System.Text.Json;
using ConstantineHub.Adapters.Godot;

namespace ConstantineHub.Core;

internal static class CanonicalKnowledge
{
    internal static string GodotRoot(GodotSettings settings) => ValidateRoot(settings.ToolsCRoot);

    internal static string BlenderRoot(TunnelProfile profile)
    {
        var wrapper = Path.GetFullPath(Environment.ExpandEnvironmentVariables(profile.McpCommand.Trim().Trim('"')));
        var project = Directory.GetParent(Path.GetDirectoryName(wrapper)!)?.FullName
            ?? throw new InvalidDataException("Cannot resolve the Blender MCP project directory.");
        // Match Blender MCP's _tools_root(): installation config, with an environment override.
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, ".tooling", "config.json")));
        var value = Environment.GetEnvironmentVariable("TOOLS_C_ROOT");
        if (string.IsNullOrWhiteSpace(value)) value = config.RootElement.GetProperty("tools_root").GetString();
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Blender tools_root is empty.");
        return ValidateRoot(Path.GetFullPath(value, project));
    }

    internal static string ResolveFolder(bool skills, string blenderProfile, string godotRoot)
    {
        var root = ValidateRoot(godotRoot);
        TunnelProfile? blender = null;
        try { blender = TunnelProfile.Load(blenderProfile); }
        catch (FileNotFoundException ex) when (ex.Message == "Tunnel profile not found.")
        {
            // An unconfigured optional Blender adapter does not prevent Godot knowledge access.
        }
        if (blender is not null)
        {
            var blenderRoot = BlenderRoot(blender);
            if (!string.Equals(root, blenderRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Canonical Tools_C roots disagree: Godot={root}; Blender={blenderRoot}.");
        }
        if (skills)
        {
            var folder = Path.Combine(root, "skills");
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
            return folder;
        }
        // There is no single primary file: human-editable docs and declarative rules
        // live in separate subdirectories. Explorer opens their common canonical root.
        foreach (var file in new[] { "docs/contracts.md", "docs/foundation.md", ".tooling/contracts.json" })
            if (!File.Exists(Path.Combine(root, file)))
                throw new FileNotFoundException("Canonical contract source is missing.", Path.Combine(root, file));
        return root;
    }

    internal static void OpenExplorer(string directory)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add(directory);
        Process.Start(start)?.Dispose();
    }

    private static string ValidateRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Canonical Tools_C root is not configured.");
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        root = new DirectoryInfo(root).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? root;
        if (!File.Exists(Path.Combine(root, "manifest.json")))
            throw new FileNotFoundException("Canonical Tools_C manifest not found.", Path.Combine(root, "manifest.json"));
        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
