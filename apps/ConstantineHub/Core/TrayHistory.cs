using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ConstantineHub.Core;

internal sealed record TrayHistoryEntry(string KeyName, string ExecutablePath, string Tooltip, Guid? IconGuid,
    bool Owned, bool CanDelete, string Reason, string Fingerprint);

/// <summary>Windows 11 history is undocumented: preview by default, fail closed, delete only one verified leaf.</summary>
internal static class TrayHistory
{
    internal const string RegistryPath = @"Control Panel\NotifyIconSettings";
    private static readonly string[] ExecutableNames = ["ConstantineHub.exe", "ConstantineHub.SmokeTests.exe", "ConHub.exe"];

    internal static IReadOnlyList<TrayHistoryEntry> Preview()
    {
        using var root = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        if (root is null) return [];
        var installed = Environment.ProcessPath ?? string.Empty;
        var active = ActiveExecutables();
        var results = new List<TrayHistoryEntry>();
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name, writable: false);
            if (key is null) continue;
            var values = ReadValues(key);
            var executable = ValueText(values, "ExecutablePath");
            var tooltip = ValueText(values, "InitialTooltip");
            if (string.IsNullOrEmpty(tooltip)) tooltip = ValueText(values, "Tooltip");
            if (!LooksRelated(executable, tooltip)) continue;
            var guid = ParseIconGuid(values.TryGetValue("IconGuid", out var iconGuid) ? iconGuid.Data : null, name);
            results.Add(Evaluate(name, executable, tooltip, guid, installed, active,
                key.SubKeyCount != 0, Fingerprint(values)));
        }
        return results;
    }

    internal static TrayHistoryEntry Evaluate(string keyName, string executable, string tooltip, Guid? guid,
        string installedExecutable, IReadOnlyCollection<string> activeExecutables, bool hasChildren, string fingerprint)
    {
        var exactInstall = SamePath(executable, installedExecutable) && IsKnownExecutable(executable);
        // Historical copies in other directories cannot be authenticated by their filename or tooltip.
        // They remain visible in diagnostics, but this program never removes them.
        var reason = !exactInstall ? "Ownership is unverified; preview only. Only the exact current installation path can be cleaned."
            : guid == TrayIdentity.IconGuid || tooltip.StartsWith("ConHub.", StringComparison.OrdinalIgnoreCase)
                ? "Current ConHub. identity is protected."
            : activeExecutables.Any(path => SamePath(path, executable)) ? "Another running process uses this executable; close it first."
            : hasChildren ? "Unexpected registry subkeys; preview only."
            : "Verified inactive legacy entry from this installation.";
        var canDelete = exactInstall && guid != TrayIdentity.IconGuid &&
            !tooltip.StartsWith("ConHub.", StringComparison.OrdinalIgnoreCase) &&
            !activeExecutables.Any(path => SamePath(path, executable)) && !hasChildren;
        return new(keyName, executable, tooltip, guid, exactInstall, canDelete, reason, fingerprint);
    }

    // Caller must display the exact preview and obtain user confirmation before calling this method.
    internal static string DeleteConfirmed(TrayHistoryEntry preview)
    {
        if (!preview.CanDelete || preview.KeyName.IndexOfAny(['\\', '/']) >= 0)
            throw new InvalidOperationException("This entry is not eligible for deletion.");
        var current = Preview().SingleOrDefault(entry => entry.KeyName == preview.KeyName);
        if (current is null || !current.CanDelete || current.Fingerprint != preview.Fingerprint ||
            !SamePath(current.ExecutablePath, preview.ExecutablePath))
            throw new InvalidOperationException("The history entry changed after preview. Refresh and review it again.");
        using var root = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("Notification icon history is unavailable.");
        Dictionary<string, SavedValue> values;
        using (var key = root.OpenSubKey(preview.KeyName, writable: false)
            ?? throw new InvalidOperationException("The history entry no longer exists."))
        {
            values = ReadValues(key);
            if (key.SubKeyCount != 0 || Fingerprint(values) != preview.Fingerprint)
                throw new InvalidOperationException("The history entry changed; nothing was deleted.");
        }
        var backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConstantineHub", "tray-history-backups");
        Directory.CreateDirectory(backupDirectory);
        var backup = Path.Combine(backupDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.WriteAllText(backup, JsonSerializer.Serialize(new
        {
            Schema = 1, Hive = "HKEY_CURRENT_USER", Key = RegistryPath + "\\" + preview.KeyName,
            CreatedUtc = DateTime.UtcNow, Values = values
        }, new JsonSerializerOptions { WriteIndented = true }));
        // Revalidate immediately before the single non-recursive deletion; never touch IsPromoted or cache roots.
        current = Preview().SingleOrDefault(entry => entry.KeyName == preview.KeyName);
        if (current is null || !current.CanDelete || current.Fingerprint != preview.Fingerprint)
            throw new InvalidOperationException("The history entry changed; backup saved, nothing deleted.");
        root.DeleteSubKey(preview.KeyName, throwOnMissingSubKey: true);
        return backup;
    }

    internal static Guid? ParseIconGuid(object? value, string keyName)
    {
        if (value is byte[] { Length: 16 } binary) return new Guid(binary);
        if (value is string text && Guid.TryParse(text, out var parsed)) return parsed;
        return Guid.TryParse(keyName, out var keyGuid) ? keyGuid : null;
    }

    internal sealed record SavedValue(string Kind, object Data);

    private static Dictionary<string, SavedValue> ReadValues(RegistryKey key)
        => key.GetValueNames().Order(StringComparer.Ordinal).ToDictionary(name => name,
            name => new SavedValue(key.GetValueKind(name).ToString(),
                key.GetValue(name, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) ?? string.Empty),
            StringComparer.Ordinal);

    private static string Fingerprint(Dictionary<string, SavedValue> values)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));

    private static string ValueText(Dictionary<string, SavedValue> values, string name)
        => values.TryGetValue(name, out var value) ? value.Data as string ?? string.Empty : string.Empty;

    private static bool IsKnownExecutable(string path)
        => ExecutableNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    private static bool LooksRelated(string path, string tooltip)
        => IsKnownExecutable(path) || tooltip.Contains("ConstantineHub", StringComparison.OrdinalIgnoreCase) ||
            tooltip.Contains("Constantine Hub", StringComparison.OrdinalIgnoreCase) || tooltip.StartsWith("ConHub.", StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string left, string right)
    {
        try
        {
            if (!Path.IsPathFullyQualified(left) || !Path.IsPathFullyQualified(right)) return false;
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static IReadOnlyCollection<string> ActiveExecutables()
    {
        var paths = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    if (process.MainModule?.FileName is { } path) paths.Add(path);
                    else if (ExecutableNames.Any(name => Path.GetFileNameWithoutExtension(name)
                        .Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase)))
                        paths.Add(Environment.ProcessPath ?? string.Empty);
                }
                catch
                {
                    // If a Hub-named process cannot be inspected, protect this installation's history too.
                    try
                    {
                        if (ExecutableNames.Any(name => Path.GetFileNameWithoutExtension(name)
                            .Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase)))
                            paths.Add(Environment.ProcessPath ?? string.Empty);
                    }
                    catch { paths.Add(Environment.ProcessPath ?? string.Empty); }
                }
            }
        }
        return paths;
    }
}
