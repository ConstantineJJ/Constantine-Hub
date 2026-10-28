using System.Security.Cryptography;
using System.Text.Json;
using ConstantineHub.Updater;
using HubUpdaterProgram = ConstantineHub.Updater.Program;

namespace ConstantineHub.SmokeTests;

internal static class PackageRetirementTests
{
    internal static void Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "hub-retirement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var valid = Prepare(fixture, "valid");
            var original = Hashes(valid.Target);
            using (var locked = new FileStream(valid.Obsolete, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ExpectFailure<IOException>(() => Apply(valid), "locked obsolete dependency blocks the complete update preflight");
                Check(Hashes(valid.Target).OrderBy(p => p.Key).SequenceEqual(original.OrderBy(p => p.Key)) &&
                    valid.Replaced.Count == 0 && valid.Created.Count == 0,
                    "obsolete-file lock changes no payload, retired files or rollback journal");
            }
            Apply(valid);
            Check(!File.Exists(valid.Obsolete) && File.Exists(Path.Combine(valid.Backup, "adapters", "mcp-helper", "licenses", "runtime-10.0.13.txt")),
                "only obsolete old-manifest files are retired and backed up");
            Check(File.ReadAllText(Path.Combine(valid.Target, "user-notes.txt")) == "user root file" &&
                File.ReadAllText(Path.Combine(valid.Target, "adapters", "mcp-helper", "user-local.txt")) == "user package file",
                "retirement preserves arbitrary user files inside and outside the package");
            Check(valid.Replaced.Contains(Path.GetRelativePath(valid.Target, valid.Obsolete)), "retired file enters the existing restoration journal");
            Check(HubUpdaterProgram.RestoreBackup(valid.Target, valid.Backup, valid.Replaced, valid.Created,
                Path.Combine(fixture, "rollback.log"), TimeSpan.FromSeconds(1)), "rollback accepts retired-file journal entries");
            Check(Hashes(valid.Target).OrderBy(p => p.Key).SequenceEqual(original.OrderBy(p => p.Key)),
                "rollback restores exact original package bytes and removes newly installed files");

            var modified = Prepare(fixture, "modified");
            File.WriteAllText(modified.Obsolete, "user-modified old dependency");
            ExpectUnchanged(modified, () => Apply(modified), "a hash-mismatched obsolete file aborts before any changes");

            foreach (var malicious in new[] { "../outside.txt", "C:/outside.txt", "licenses/../../outside.txt", "licenses\\outside.txt" })
            {
                var escape = Prepare(fixture, "escape-" + Guid.NewGuid().ToString("N"));
                AddManifestEntry(escape.Target, malicious);
                ExpectUnchanged(escape, () => Apply(escape), "manifest path escapes or aliases are rejected before mutations");
            }
            var badSource = Prepare(fixture, "source-escape");
            AddManifestEntry(badSource.Source, "../outside.txt");
            ExpectUnchanged(badSource, () => Apply(badSource), "incoming manifest paths are validated before retirement");

            var linked = Prepare(fixture, "link");
            var outside = Path.Combine(fixture, "outside");
            Directory.CreateDirectory(outside);
            var outsideFile = Path.Combine(outside, "runtime-10.0.13.txt");
            File.WriteAllText(outsideFile, "original obsolete runtime license");
            var licenses = Path.GetDirectoryName(linked.Obsolete)!;
            File.Delete(linked.Obsolete);
            Directory.Delete(licenses);
            Directory.CreateSymbolicLink(licenses, outside);
            try
            {
                ExpectFailure<InvalidDataException>(() => Apply(linked), "obsolete dependency through a directory link is rejected");
                Check(File.ReadAllText(outsideFile) == "original obsolete runtime license" && linked.Replaced.Count == 0 && linked.Created.Count == 0,
                    "link rejection preserves external bytes and rollback journal");
            }
            finally { Directory.Delete(licenses); }
            Console.WriteLine("PACKAGE RETIREMENT / RECOVERY PASS");
        }
        finally { Directory.Delete(fixture, recursive: true); }
    }

    private sealed record Fixture(string Source, string Target, string Backup, string Obsolete,
        List<string> Replaced, List<string> Created);

    private static Fixture Prepare(string root, string name)
    {
        var source = Path.Combine(root, name, "source");
        var target = Path.Combine(root, name, "target");
        var backup = Path.Combine(root, name, "backup");
        foreach (var (directory, old) in new[] { (target, true), (source, false) })
        {
            var package = Path.Combine(directory, "adapters", "mcp-helper");
            Directory.CreateDirectory(Path.Combine(package, "licenses"));
            File.WriteAllText(Path.Combine(directory, "ConstantineHub.dll"), old ? "old Hub" : "new Hub");
            File.WriteAllText(Path.Combine(package, "McpHelper.exe"), old ? "old Helper" : "new Helper");
            File.WriteAllText(Path.Combine(package, "licenses", old ? "runtime-10.0.13.txt" : "runtime-10.0.14.txt"),
                old ? "original obsolete runtime license" : "current runtime license");
            var manifest = new { kind = "mcp-helper-package", schemaVersion = 1, version = old ? "0.1.1" : "0.1.2",
                entrypoint = "McpHelper.exe", files = Hashes(package) };
            File.WriteAllText(Path.Combine(package, "package-manifest.json"), JsonSerializer.Serialize(manifest));
        }
        File.WriteAllText(Path.Combine(target, "user-notes.txt"), "user root file");
        File.WriteAllText(Path.Combine(target, "adapters", "mcp-helper", "user-local.txt"), "user package file");
        return new(source, target, backup, Path.Combine(target, "adapters", "mcp-helper", "licenses", "runtime-10.0.13.txt"), [], []);
    }

    private static void AddManifestEntry(string root, string name)
    {
        var path = Path.Combine(root, "adapters", "mcp-helper", "package-manifest.json");
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        document["files"]![name] = new string('a', 64);
        File.WriteAllText(path, document.ToJsonString());
    }

    private static void Apply(Fixture fixture) => HubUpdaterProgram.ApplyFiles(fixture.Source, fixture.Target,
        fixture.Backup, fixture.Replaced, fixture.Created, TimeSpan.FromMilliseconds(150));

    private static void ExpectUnchanged(Fixture fixture, Action action, string description)
    {
        var before = Hashes(fixture.Target);
        ExpectFailure<InvalidDataException>(action, description);
        Check(Hashes(fixture.Target).OrderBy(p => p.Key).SequenceEqual(before.OrderBy(p => p.Key)) &&
            fixture.Replaced.Count == 0 && fixture.Created.Count == 0, "rejected retirement preserves every installation byte");
    }

    private static Dictionary<string, string> Hashes(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'),
            path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));

    private static void ExpectFailure<T>(Action action, string description) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, description); return; }
        throw new Exception("FAIL: " + description);
    }

    private static void Check(bool ok, string description)
    {
        if (!ok) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
