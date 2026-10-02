using System.Text;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

namespace ConstantineHub.Core;

internal static class TunnelPluginInstaller
{
    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    internal static async Task InstallAsync(string executable, string? codexHome = null,
        Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable)) throw new FileNotFoundException("tunnel-client.exe was not found.", executable);
        codexHome = Path.GetFullPath(codexHome ?? Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
        await InstallGate.WaitAsync(cancellationToken);
        var staging = Path.Combine(codexHome, "plugins", ".hub-install-" + Guid.NewGuid().ToString("N"));
        var bundle = Path.Combine(staging, "bundle");
        var backup = Path.Combine(staging, "previous");
        var target = Path.Combine(codexHome, "plugins", "cache", "debug", "tunnel-mcp", "local");
        var configPath = Path.Combine(codexHome, "config.toml");
        var movedPrevious = false;
        var movedBundle = false;
        var committed = false;
        try
        {
            // Export first: the upstream installer deletes the installed plugin before checking
            // Unix execute bits, which incorrectly rejects a working Windows .exe.
            var result = await ProcessCapture.RunAsync(executable,
                ["codex", "plugin", "export", "--dir", bundle], cancellationToken);
            var windowsModeBug = OperatingSystem.IsWindows() && result.ExitCode != 0 &&
                result.Output.Trim().Equals("tunnel-client binary is not executable: " + executable,
                    StringComparison.OrdinalIgnoreCase);
            if (result.ExitCode != 0 && !windowsModeBug)
                throw new InvalidOperationException($"Plugin export exited with code {result.ExitCode}.\n{result.Output}");
            ValidateBundle(bundle);
            File.WriteAllText(Path.Combine(bundle, ".tunnel-client-bin"), executable + "\n", new UTF8Encoding(false));
            var before = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
            var after = EnablePlugin(before);
            var pendingConfig = Path.Combine(staging, "config.toml");
            File.WriteAllText(pendingConfig, after, new UTF8Encoding(false));
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
                movedPrevious = true;
            }
            Directory.Move(bundle, target);
            movedBundle = true;
            if ((File.Exists(configPath) ? File.ReadAllText(configPath) : "") != before)
                throw new IOException("Codex settings changed during installation. Please retry.");
            File.Move(pendingConfig, configPath, overwrite: true);
            committed = true;
        }
        catch
        {
            if (movedBundle) Directory.Delete(target, recursive: true);
            if (movedPrevious) Directory.Move(backup, target);
            throw;
        }
        finally
        {
            // Cleanup must not turn a completed installation into an apparent failure.
            try
            {
                if (Directory.Exists(staging) && (committed || !Directory.Exists(backup)))
                    Directory.Delete(staging, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            InstallGate.Release();
        }
        log?.Invoke("Tunnel MCP plugin installed and enabled. Restart Codex to load it.");
    }

    private static void ValidateBundle(string bundle)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, ".codex-plugin", "plugin.json")));
        if (manifest.RootElement.GetProperty("name").GetString() != "tunnel-mcp")
            throw new InvalidDataException("The exported plugin is not Tunnel MCP.");
        using var mcp = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, ".mcp.json")));
        if (!mcp.RootElement.GetProperty("mcpServers").TryGetProperty("tunnelMcp", out _) ||
            !File.Exists(Path.Combine(bundle, "mcp", "server.cjs")))
            throw new InvalidDataException("The exported Tunnel MCP plugin is incomplete.");
    }

    internal static string EnablePlugin(string config)
    {
        // Use a TOML parser so quoted keys, nested permissions and other settings survive.
        var model = Toml.ToModel(config);
        if (!model.TryGetValue("plugins", out var pluginsValue))
            model["plugins"] = pluginsValue = new TomlTable();
        if (pluginsValue is not TomlTable plugins)
            throw new InvalidDataException("Codex plugins settings must be a TOML table.");
        if (!plugins.TryGetValue("tunnel-mcp@debug", out var pluginValue))
            plugins["tunnel-mcp@debug"] = pluginValue = new TomlTable();
        if (pluginValue is not TomlTable plugin)
            throw new InvalidDataException("Tunnel MCP settings must be a TOML table.");
        plugin["enabled"] = true;
        var updated = Toml.FromModel(model);
        _ = Toml.ToModel(updated);
        return updated;
    }
}
