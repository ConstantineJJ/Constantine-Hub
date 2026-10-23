using System.Security.Cryptography;
using System.Text;

namespace McpHelper.Core;

internal sealed record InputFile(string FullPath, string RelativePath, byte[] Bytes, string Text, string Hash);

internal sealed class SourceScope
{
    public string Root { get; }
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".svn", "bin", "obj", "node_modules", ".venv", "venv", "__pycache__", ".vs" };

    public SourceScope(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(Root)) throw new ArgumentException("Workspace directory does not exist.");
        RejectReparse(Root);
    }

    public string Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new ScopeException("E_SCOPE", "A relative workspace path is required.");
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ScopeException("E_SCOPE", "Path is outside the configured workspace.");
        RejectReparse(path);
        return path;
    }

    public static void RejectReparse(string path)
    {
        for (var p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new ScopeException("E_SCOPE", "Symbolic links and junctions are not permitted in a workspace path.");
    }

    public List<InputFile> Capture(CancellationToken cancellationToken)
    {
        RejectReparse(Root);
        var files = new List<InputFile>();
        var pending = new Stack<string>();
        pending.Push(Root);
        long total = 0;
        var directories = 0;
        while (pending.TryPop(out var dir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++directories > 10000) throw new ScopeException("E_LIMIT", "Workspace directory limit exceeded.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0 && Excluded.Contains(Path.GetFileName(entry))) continue;
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ScopeException("E_SCOPE", "Workspace contains a symbolic link or junction.");
                if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); continue; }
                var ext = Path.GetExtension(entry);
                if (ext != ".cs" && ext != ".csproj") continue;
                if (files.Count >= 1000 || new FileInfo(entry).Length > 1024 * 1024)
                    throw new ScopeException("E_LIMIT", "Workspace file count or size limit exceeded.");
                var bytes = File.ReadAllBytes(entry);
                total += bytes.Length;
                if (bytes.Length > 1024 * 1024 || total > 32 * 1024 * 1024)
                    throw new ScopeException("E_LIMIT", "Workspace byte budget exceeded.");
                string text;
                try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
                catch (DecoderFallbackException) { throw new ScopeException("E_ENCODING", "Source files must be UTF-8 (optional BOM)."); }
                files.Add(new(entry, Path.GetRelativePath(Root, entry).Replace('\\', '/'), bytes, text,
                    Convert.ToHexStringLower(SHA256.HashData(bytes))));
            }
        }
        return files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();
    }

    public static string Snapshot(List<InputFile> files) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join('\n', files.Select(f => f.RelativePath + ":" + f.Hash)))));
}
