using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConstantineHub.Core;

internal sealed record HubUpdateInfo(
    string Tag,
    Version Version,
    string ZipUrl,
    string Sha256Url,
    string HtmlUrl);

internal sealed class UpdateService
{
    private const string ReleasesUrl = "https://api.github.com/repos/ConstantineJJ/Constantine-Hub/releases?per_page=20";
    private const string ZipAssetName = "ConstantineHub-win-x64.zip";
    private const string ShaAssetName = "ConstantineHub-win-x64.sha256";

    private static readonly HttpClient Http = CreateHttpClient();

    internal async Task<HubUpdateInfo?> CheckForStableUpdateAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(ReleasesUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                continue;
            if (release.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
                continue;

            var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!TryParseVersion(tag, out var version) || version <= currentVersion)
                continue;

            string? zipUrl = null;
            string? shaUrl = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var url = asset.GetProperty("browser_download_url").GetString();
                if (string.Equals(name, ZipAssetName, StringComparison.OrdinalIgnoreCase))
                    zipUrl = url;
                else if (string.Equals(name, ShaAssetName, StringComparison.OrdinalIgnoreCase))
                    shaUrl = url;
            }

            if (zipUrl is null || shaUrl is null)
                continue;

            var htmlUrl = release.TryGetProperty("html_url", out var html)
                ? html.GetString() ?? string.Empty
                : string.Empty;
            return new HubUpdateInfo(tag, version, zipUrl, shaUrl, htmlUrl);
        }

        return null;
    }

    internal async Task StageAndLaunchAsync(HubUpdateInfo update, Version currentVersion, CancellationToken cancellationToken = default)
    {
        var updatesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConstantineHub",
            "updates");
        Directory.CreateDirectory(updatesRoot);

        var safeTag = Regex.Replace(update.Tag, "[^A-Za-z0-9._-]", "_");
        var stageRoot = Path.Combine(updatesRoot, safeTag + "-" + Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(stageRoot, "payload");
        Directory.CreateDirectory(payload);

        var zipPath = Path.Combine(stageRoot, ZipAssetName);
        var shaPath = Path.Combine(stageRoot, ShaAssetName);

        await DownloadFileAsync(update.ZipUrl, zipPath, cancellationToken);
        await DownloadFileAsync(update.Sha256Url, shaPath, cancellationToken);

        var expectedHash = (await File.ReadAllTextAsync(shaPath, cancellationToken))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(expectedHash))
            throw new InvalidDataException("Release checksum file is empty.");

        var actualHash = await ComputeSha256Async(zipPath, cancellationToken);
        if (!string.Equals(expectedHash.Trim(), actualHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Update SHA-256 mismatch. Expected {expectedHash}, got {actualHash}.");

        ZipFile.ExtractToDirectory(zipPath, payload, overwriteFiles: true);

        var updaterPath = Path.Combine(payload, "ConstantineHub.Updater.exe");
        if (!File.Exists(updaterPath))
            throw new FileNotFoundException("The release does not contain ConstantineHub.Updater.exe.", updaterPath);

        var targetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            UseShellExecute = false,
            WorkingDirectory = payload
        };
        startInfo.ArgumentList.Add("--source");
        startInfo.ArgumentList.Add(payload);
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(targetDirectory);
        startInfo.ArgumentList.Add("--pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("--restart");
        startInfo.ArgumentList.Add("ConstantineHub.exe");
        startInfo.ArgumentList.Add("--from");
        startInfo.ArgumentList.Add($"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}");
        startInfo.ArgumentList.Add("--to");
        startInfo.ArgumentList.Add(update.Tag);

        Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start ConstantineHub.Updater.exe.");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ConstantineHub", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var match = Regex.Match(tag, @"^v?(\d+)\.(\d+)\.(\d+)$", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            version = new Version(0, 0, 0);
            return false;
        }

        version = new Version(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value));
        return true;
    }
}
