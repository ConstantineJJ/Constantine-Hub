using System.Text.Json;

namespace McpHelper;

public sealed record HelperConfig(string WorkspaceRoot, int Port = 19273, string Token = "", string InstanceId = "standalone")
{
    public static HelperConfig Read(string path)
    {
        var info = new FileInfo(Path.GetFullPath(path));
        if (info.Length > 16384) throw new ArgumentException("Config is too large.");
        return JsonSerializer.Deserialize<HelperConfig>(File.ReadAllText(info.FullName),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new ArgumentException("Config is invalid.");
    }

    public void Validate(bool http)
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRoot) || !Path.IsPathFullyQualified(WorkspaceRoot))
            throw new ArgumentException("An absolute WorkspaceRoot is required.");
        if (Port is < 1024 or > 65535 || string.IsNullOrEmpty(InstanceId) || InstanceId.Length > 128 || InstanceId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Invalid port or instance identity.");
        if (http && (string.IsNullOrEmpty(Token) || Token.Length < 32 || Token.Length > 256 || Token.Any(c => !char.IsAsciiLetterOrDigit(c))))
            throw new ArgumentException("HTTP requires a 32-256 character alphanumeric Token in the local config file.");
    }
}
