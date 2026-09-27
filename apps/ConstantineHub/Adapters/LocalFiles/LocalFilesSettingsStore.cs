using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConstantineHub.Adapters.LocalFiles;

internal sealed record LocalFilesRootRule(string Path, bool Read, bool Write, bool Delete)
{
    internal IEnumerable<string> PermissionNames()
    {
        if (Read) yield return "read";
        if (Write) yield return "write";
        if (Delete) yield return "delete";
    }
}

internal sealed record LocalFilesSummary(
    bool ConfigExists,
    bool ConfigValid,
    int RootCount,
    bool AnyWrite,
    bool AnyDelete,
    string Message);

internal static class LocalFilesSettingsStore
{
    internal static string ConfigPath
    {
        get
        {
            var explicitPath = Environment.GetEnvironmentVariable("CONSTANTINE_FILES_CONFIG");
            if (!string.IsNullOrWhiteSpace(explicitPath))
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitPath));

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "ConstantineHub", "local-files-mcp.json");
        }
    }

    internal static IReadOnlyList<LocalFilesRootRule> LoadRules()
    {
        var document = LoadDocument();
        if (document["schema_version"]?.GetValue<int>() != 1)
            throw new InvalidDataException("Local Files MCP schema_version must be 1.");

        if (document["allowed_roots"] is not JsonArray array || array.Count == 0)
            throw new InvalidDataException("Local Files MCP allowed_roots must be a non-empty array.");

        var rules = new List<LocalFilesRootRule>();
        foreach (var item in array)
        {
            if (item is not JsonObject rule)
                throw new InvalidDataException("Each allowed_roots entry must be an object.");

            var path = rule["path"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("An allowed root is missing its path.");

            var permissions = rule["permissions"] as JsonArray;
            if (permissions is null || permissions.Count == 0)
                throw new InvalidDataException($"Allowed root '{path}' has no permissions.");

            var names = permissions
                .Select(x => x?.GetValue<string>() ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = names.Where(x =>
                !string.Equals(x, "read", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(x, "write", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(x, "delete", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (unknown.Length > 0)
                throw new InvalidDataException($"Allowed root '{path}' has unsupported permissions: {string.Join(", ", unknown)}");

            rules.Add(new LocalFilesRootRule(
                NormalizeExistingDirectory(path),
                names.Contains("read"),
                names.Contains("write"),
                names.Contains("delete")));
        }

        ValidateRules(rules);
        return rules;
    }

    internal static LocalFilesSummary GetSummary()
    {
        if (!File.Exists(ConfigPath))
            return new(false, false, 0, false, false, $"Config not found: {ConfigPath}");

        try
        {
            var rules = LoadRules();
            return new(
                true,
                true,
                rules.Count,
                rules.Any(x => x.Write),
                rules.Any(x => x.Delete),
                $"{rules.Count} root(s) configured");
        }
        catch (Exception ex)
        {
            return new(true, false, 0, false, false, ex.Message);
        }
    }

    internal static IReadOnlyList<string> ValidateRules(IEnumerable<LocalFilesRootRule> input)
    {
        var rules = input.ToList();
        if (rules.Count == 0)
            throw new InvalidDataException("At least one allowed root is required.");

        var normalized = new List<LocalFilesRootRule>(rules.Count);
        foreach (var rule in rules)
        {
            if (!rule.Read && !rule.Write && !rule.Delete)
                throw new InvalidDataException($"Allowed root '{rule.Path}' must grant at least one permission.");

            normalized.Add(rule with { Path = NormalizeExistingDirectory(rule.Path) });
        }

        var duplicates = normalized
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidDataException("Duplicate canonical root(s): " + string.Join(", ", duplicates));

        var warnings = new List<string>();
        for (var i = 0; i < normalized.Count; i++)
        {
            for (var j = i + 1; j < normalized.Count; j++)
            {
                if (IsStrictDescendant(normalized[i].Path, normalized[j].Path) ||
                    IsStrictDescendant(normalized[j].Path, normalized[i].Path))
                {
                    warnings.Add(
                        $"Nested roots: '{normalized[i].Path}' and '{normalized[j].Path}'. " +
                        "Permissions are additive across overlapping roots; a nested rule cannot revoke a permission granted by its parent.");
                }
            }
        }

        return warnings;
    }

    internal static void SaveRules(IEnumerable<LocalFilesRootRule> input)
    {
        var rules = input
            .Select(rule => rule with { Path = NormalizeExistingDirectory(rule.Path) })
            .ToList();
        ValidateRules(rules);

        var document = LoadDocument();
        if (document["schema_version"]?.GetValue<int>() != 1)
            throw new InvalidDataException("Refusing to write an unsupported Local Files MCP config schema.");

        var roots = new JsonArray();
        foreach (var rule in rules)
        {
            var permissions = new JsonArray();
            foreach (var permission in rule.PermissionNames())
                permissions.Add(permission);

            roots.Add(new JsonObject
            {
                ["path"] = rule.Path,
                ["permissions"] = permissions
            });
        }

        document["allowed_roots"] = roots;
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

        var temp = ConfigPath + ".tmp";
        var backup = ConfigPath + ".bak";
        var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temp, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(ConfigPath))
            File.Replace(temp, ConfigPath, backup, ignoreMetadataErrors: true);
        else
            File.Move(temp, ConfigPath);
    }

    internal static string NormalizeExistingDirectory(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            throw new InvalidDataException("Folder path cannot be empty.");

        var expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim());
        if (!Path.IsPathFullyQualified(expanded))
            throw new InvalidDataException($"Folder path must be absolute: {rawPath}");

        var full = Path.GetFullPath(expanded);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Allowed root does not exist: {full}");

        var info = new DirectoryInfo(full);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is DirectoryInfo targetDirectory)
                full = Path.GetFullPath(targetDirectory.FullName);
            else
                throw new InvalidDataException($"Could not resolve reparse root: {full}");
        }

        var driveRoot = Path.GetPathRoot(full);
        if (!string.IsNullOrWhiteSpace(driveRoot) &&
            string.Equals(full, driveRoot, StringComparison.OrdinalIgnoreCase))
            return driveRoot;

        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static JsonObject LoadDocument()
    {
        if (!File.Exists(ConfigPath))
            throw new FileNotFoundException("Local Files MCP config was not found.", ConfigPath);

        var text = File.ReadAllText(ConfigPath, Encoding.UTF8);
        return JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidDataException("Local Files MCP config root must be a JSON object.");
    }

    private static bool IsStrictDescendant(string parent, string candidate)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        if (relative == ".")
            return false;
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
