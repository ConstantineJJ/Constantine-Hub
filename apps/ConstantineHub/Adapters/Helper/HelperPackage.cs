using System.Security.Cryptography;
using System.Text.Json;

namespace ConstantineHub.Adapters.Helper;

internal static class HelperPackage
{
    internal static string Verify(string executable)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        var path = Path.Combine(root, "package-manifest.json");
        if (!File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Install a complete MCP Helper package with its manifest.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(path));
        var m = manifest.RootElement;
        if (m.GetProperty("kind").GetString() != "mcp-helper-package" || m.GetProperty("schemaVersion").GetInt32() != 1 ||
            m.GetProperty("version").GetString() != "0.1.0" || m.GetProperty("entrypoint").GetString() != "McpHelper.exe" ||
            Path.GetFileName(executable) != "McpHelper.exe") throw new InvalidDataException("Unsupported Helper package identity.");
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in m.GetProperty("files").EnumerateObject())
        {
            var name = file.Name;
            if (expected.Count >= 1500 || !expected.Add(name) || name.Contains('\\') || name.Contains(':') ||
                Path.IsPathRooted(name) || name.Split('/').Any(s => s is "" or "." or ".."))
                throw new InvalidDataException("Invalid Helper manifest path.");
            var full = Path.GetFullPath(Path.Combine(root, name));
            RejectReparse(full);
            if (!File.Exists(full)) throw new InvalidDataException("Helper package file is missing.");
            using var stream = File.OpenRead(full);
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), file.Value.GetString(), StringComparison.Ordinal))
                throw new InvalidDataException("Helper package checksum mismatch.");
        }
        if (!expected.Contains("McpHelper.exe")) throw new InvalidDataException("Helper entrypoint is missing from manifest.");
        var actual = Enumerate(root).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Where(p => p != "package-manifest.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(expected)) throw new InvalidDataException("Helper package contains unexpected files.");
        return "0.1.0";
    }

    private static IEnumerable<string> Enumerate(string root)
    {
        RejectReparse(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            RejectReparse(entry);
            if (Directory.Exists(entry)) { foreach (var child in Enumerate(entry)) yield return child; }
            else yield return entry;
        }
    }

    private static void RejectReparse(string path)
    {
        for (var p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Helper package must not contain links or junctions.");
    }
}
