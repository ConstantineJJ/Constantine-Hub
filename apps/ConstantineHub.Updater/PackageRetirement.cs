using System.Security.Cryptography;
using System.Text.Json;

namespace ConstantineHub.Updater;

/// <summary>Only an old Helper manifest can authorize retiring a package-owned file.</summary>
internal static class PackageRetirement
{
    private const string HelperRoot = "adapters/mcp-helper";
    private const string Manifest = HelperRoot + "/package-manifest.json";
    internal sealed record RetiredFile(string Relative, string Target, string Backup);

    internal static IReadOnlyList<RetiredFile> Plan(string source, string target, string backupRoot)
    {
        var nextManifest = Resolve(source, Manifest);
        var previousManifest = Resolve(target, Manifest);
        // Releases before Helper was introduced have no package files to retire.
        if (!File.Exists(nextManifest) || !File.Exists(previousManifest)) return [];
        var next = ReadManifest(nextManifest);
        var previous = ReadManifest(previousManifest);
        var retired = new List<RetiredFile>();
        foreach (var file in previous.Where(file => !next.ContainsKey(file.Key)))
        {
            var relative = HelperRoot + "/" + file.Key;
            var path = Resolve(target, relative);
            var backup = Resolve(backupRoot, relative);
            if (!File.Exists(path)) continue;
            using var stream = File.OpenRead(path);
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), file.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Obsolete Helper package file was modified; update left it untouched: " + path);
            retired.Add(new(relative.Replace('/', Path.DirectorySeparatorChar), path, backup));
        }
        return retired;
    }

    internal static void Retire(RetiredFile file, IList<string> replaced, TimeSpan timeout)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file.Backup)!);
        File.Copy(file.Target, file.Backup, overwrite: true);
        UpdateFiles.Retry(() => File.Delete(file.Target), file.Target, timeout);
        // A successfully retired file uses the same restoration journal as a replacement.
        replaced.Add(file.Relative);
    }

    private static Dictionary<string, string> ReadManifest(string path)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Helper package manifest is too large.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var manifest = document.RootElement;
        if (manifest.GetProperty("kind").GetString() != "mcp-helper-package" ||
            manifest.GetProperty("schemaVersion").GetInt32() != 1 ||
            manifest.GetProperty("entrypoint").GetString() != "McpHelper.exe")
            throw new InvalidDataException("Unsupported Helper package manifest.");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.GetProperty("files").EnumerateObject())
        {
            ValidateRelative(file.Name);
            var hash = file.Value.GetString();
            if (files.Count >= 1500 || hash is null || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit) ||
                !files.TryAdd(file.Name, hash))
                throw new InvalidDataException("Invalid Helper package manifest file entry.");
        }
        if (!files.ContainsKey("McpHelper.exe"))
            throw new InvalidDataException("Helper package manifest has no entrypoint.");
        return files;
    }

    internal static string Resolve(string root, string relative)
    {
        ValidateRelative(relative.Replace(Path.DirectorySeparatorChar, '/'));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
        if (!full.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update package path escaped its directory.");
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Update package paths must not contain links or junctions: " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return full;
    }

    private static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || Path.IsPathRooted(path) ||
            path.Contains('\\') || path.Contains(':') || path.Split('/').Any(segment =>
                segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Invalid relative Helper package path.");
    }
}
