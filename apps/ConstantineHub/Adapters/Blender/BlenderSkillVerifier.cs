using ConstantineHub.Core;

namespace ConstantineHub.Adapters.Blender;

internal sealed record BlenderSkillVerification(
    bool Success,
    int SourceCount,
    int VerifiedCount,
    string? SourceDirectory,
    string? TargetDirectory,
    string Message);

internal static class BlenderSkillVerifier
{
    internal static BlenderSkillVerification Verify(string profileName)
    {
        TunnelProfile profile;
        try
        {
            profile = TunnelProfile.Load(profileName);
        }
        catch (Exception ex)
        {
            return new(false, 0, 0, null, null, "Cannot load tunnel profile: " + ex.Message);
        }

        var wrapper = NormalizePath(profile.McpCommand);
        if (string.IsNullOrWhiteSpace(wrapper) || !File.Exists(wrapper))
            return new(false, 0, 0, null, null, "Configured Blender MCP wrapper was not found: " + profile.McpCommand);

        var serverDirectory = Path.GetDirectoryName(wrapper);
        if (string.IsNullOrWhiteSpace(serverDirectory))
            return new(false, 0, 0, null, null, "Could not resolve Blender MCP server directory.");

        var repositoryDirectory = Directory.GetParent(serverDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(repositoryDirectory))
            return new(false, 0, 0, null, null, "Could not resolve Blender MCP repository directory.");

        var source = Path.Combine(repositoryDirectory, "skills");
        var target = Path.Combine(serverDirectory, "skills");
        try { _ = CanonicalKnowledge.BlenderRoot(profile); }
        catch (Exception ex)
        {
            return new(false, 0, 0, source, target, "Cannot resolve canonical Tools_C: " + ex.Message);
        }
        if (!Directory.Exists(source))
            return new(false, 0, 0, source, target, "Blender source skills directory does not exist.");
        if (!Directory.Exists(target))
            return new(false, 0, 0, source, target, "Blender MCP router skills directory does not exist.");

        var sourceSkills = Discover(source);
        if (sourceSkills.Count == 0)
            return new(false, 0, 0, source, target, "No valid source skills containing SKILL.md were found.");

        var verified = 0;
        try
        {
            foreach (var skill in sourceSkills)
            {
                var targetFile = Path.Combine(target, skill.Name, "SKILL.md");
                if (!File.Exists(targetFile))
                    return new(false, sourceSkills.Count, verified, source, target,
                        $"Missing MCP skill router: {targetFile}");

                if (!File.ReadAllBytes(skill.SkillFile).AsSpan().SequenceEqual(File.ReadAllBytes(targetFile)))
                    return new(false, sourceSkills.Count, verified, source, target,
                        $"MCP skill router differs from source: {targetFile}");

                verified++;
            }
        }
        catch (Exception ex)
        {
            return new(false, sourceSkills.Count, verified, source, target,
                "Cannot verify Blender skill routers: " + ex.Message);
        }

        return new(true, sourceSkills.Count, verified, source, target,
            $"Verified {verified}/{sourceSkills.Count} Blender MCP skill routers.");
    }

    private static IReadOnlyList<(string Name, string SkillFile)> Discover(string root)
    {
        var result = new List<(string Name, string SkillFile)>();
        foreach (var directory in Directory.GetDirectories(root).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var skillFile = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(skillFile) || new FileInfo(skillFile).Length <= 0)
                continue;
            result.Add((Path.GetFileName(directory), skillFile));
        }
        return result;
    }

    private static string? NormalizePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        try
        {
            return Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(command.Trim().Trim('"'))
                    .Replace('/', Path.DirectorySeparatorChar));
        }
        catch
        {
            return null;
        }
    }
}
