using System.Security.Cryptography;
using System.Text.Json;

namespace ConstantineHub.Adapters.Helper;

internal sealed record HelperSettings(string WorkspaceRoot = "", string Executable = "", int Port = 19273,
    bool IncludeInStartAll = false, string Token = "")
{
    internal static string ConfigPath => Environment.GetEnvironmentVariable("CONSTANTINE_HELPER_SETTINGS")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConstantineHub", "helper.json");
    internal static string BundledExecutable => Path.Combine(AppContext.BaseDirectory, "adapters", "mcp-helper", "McpHelper.exe");
    internal string ExecutablePath => string.IsNullOrWhiteSpace(Executable) ? BundledExecutable : Path.GetFullPath(Executable);

    internal static HelperSettings Load()
    {
        if (!File.Exists(ConfigPath)) return new();
        if (new FileInfo(ConfigPath).Length > 16384) throw new InvalidDataException("Helper settings are too large.");
        return JsonSerializer.Deserialize<HelperSettings>(File.ReadAllText(ConfigPath))
            ?? throw new InvalidDataException("Helper settings are invalid.");
    }

    internal void Save()
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ConfigPath))!);
        var temp = ConfigPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, ConfigPath, overwrite: true);
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRoot) || !Path.IsPathFullyQualified(WorkspaceRoot) || !Directory.Exists(WorkspaceRoot))
            throw new InvalidDataException("Select an existing absolute Helper workspace directory.");
        if (Port is < 1024 or > 65535 || Port is 9876 or 6262 or 6263)
            throw new InvalidDataException("Choose a separate Helper port between 1024 and 65535.");
        if (string.IsNullOrEmpty(Token) || Token.Length < 32 || Token.Length > 256 || Token.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new InvalidDataException("Helper connection token is invalid.");
    }

    internal static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}
