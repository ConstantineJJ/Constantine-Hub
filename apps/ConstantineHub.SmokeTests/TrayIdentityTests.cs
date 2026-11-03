using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class TrayIdentityTests
{
    internal static void Run()
    {
        var calls = new List<uint>();
        using (var tray = new StableTrayIcon((operation, _) => { calls.Add(operation); return true; }))
        {
            Check(calls.Count == 0, "constructing tray does not register a Windows icon");
            tray.Icon = SystemIcons.Application;
            tray.Text = "ConHub.";
            tray.Visible = true;
            tray.Visible = true;
            Check(calls.Count(op => op == 0) == 1, "repeated visible assignment adds only one tray icon");
            tray.Text = "ConHub. ready";
            Check(calls.Count(op => op == 0) == 1 && calls.Contains(1), "status changes modify the existing GUID icon");
            tray.ExplorerRestarted();
            Check(calls.Count(op => op == 0) == 2, "Explorer restart recreates exactly one icon with the same identity");
            tray.Visible = false;
            tray.Visible = false;
            Check(calls.Count(op => op == 2) == 1, "hiding a tray icon deletes it once");
        }
        Check(calls.Count(op => op == 2) == 1, "disposing an already hidden tray does not repeat shell deletion");
        calls.Clear();
        using (var tray = new StableTrayIcon((operation, _) => { calls.Add(operation); return true; }))
        { tray.Visible = true; }
        Check(calls.SequenceEqual(new uint[] { 0, 2 }), "disposing a visible tray deletes its shell registration");
        var failures = 0;
        using (var tray = new StableTrayIcon((_, _) => { failures++; return false; }))
        {
            tray.Visible = true;
            tray.Visible = true;
            Check(!tray.IsRegistered && failures == 1 && tray.LastError is not null,
                "registration failure is reported without creating an alternative GUID or repeated registrations");
        }
        Check(TrayIdentity.AppUserModelId == "Constantine.ConHub" && TrayIdentity.IconGuid != Guid.Empty,
            "tray GUID and AppUserModelID are fixed and independent of display version");
        Check(!TrayIdentity.ShouldEnableTray(new Dictionary<string, string> { ["isolated-instance"] = "true" }),
            "isolated smoke/update fixtures cannot register tray icons");
        Check(!TrayIdentity.ShouldEnableTray(new Dictionary<string, string> { ["no-tray"] = "true" }),
            "no-tray launch suppresses shell registration");

        Check(TrayHistory.ParseIconGuid(TrayIdentity.IconGuid.ToByteArray(), "unrelated") == TrayIdentity.IconGuid &&
            TrayHistory.ParseIconGuid(TrayIdentity.IconGuid.ToString(), "unrelated") == TrayIdentity.IconGuid,
            "registry GUID parsing protects both REG_BINARY and string identities");
        Check(TrayHistory.ParseIconGuid(new byte[3], "123456") is null,
            "unknown registry GUID formats remain unverified");
        var installed = Path.GetFullPath(@"C:\Apps\ConHub\ConstantineHub.exe");
        var legacy = TrayHistory.Evaluate("legacy", installed, "ConstantineHub", null, installed, [], false, "v1");
        Check(legacy.CanDelete && legacy.Owned, "exact installation path establishes ownership of an inactive legacy entry");
        Check(!TrayHistory.Evaluate("current", installed, "ConHub.", TrayIdentity.IconGuid, installed, [], false, "v1").CanDelete,
            "current GUID is protected from cleanup");
        Check(!TrayHistory.Evaluate("active", installed, "ConstantineHub", null, installed, [installed], false, "v1").CanDelete,
            "entries belonging to another live process are protected");
        var unrelated = TrayHistory.Evaluate("other", @"C:\Other\ConstantineHub.exe", "ConstantineHub", null,
            installed, [], false, "v1");
        Check(!unrelated.Owned && !unrelated.CanDelete, "matching filename and tooltip alone never authorize history deletion");
        Check(!TrayHistory.Evaluate("unknown", @"C:\Old\ConstantineHub.SmokeTests.exe", "ConstantineHub.SmokeTests", null,
            installed, [], false, "v1").CanDelete, "unverifiable historical smoke records remain preview-only");
        Check(!TrayHistory.Evaluate("nested", installed, "ConstantineHub", null, installed, [], true, "v1").CanDelete,
            "unexpected registry child keys are protected against recursive deletion");
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
