using System.Reflection;
using ConstantineHub;
using ConstantineHub.Core;
using ConstantineHub.Adapters.LocalFiles;

namespace ConstantineHub.SmokeTests;

internal static class WorkspaceFeaturesTests
{
    internal static void FileHandling(string fixture)
    {
        Environment.SetEnvironmentVariable("CONSTANTINE_HUB_INBOX", Path.Combine(fixture, "inbox"));
        var source = Path.Combine(fixture, "asset.glb");
        var bytes = new byte[] { 0x67, 0x6c, 0x54, 0x46, 0, 255, 128, 1 };
        File.WriteAllBytes(source, bytes);
        Check(FileInbox.EnsureAgentAccess(FileInbox.Root), "Inbox folder is registered for agent access");
        Check(!FileInbox.EnsureAgentAccess(FileInbox.Root), "Inbox registration is idempotent");
        var copied = FileInbox.ImportAsync([source]).GetAwaiter().GetResult().Single();
        Check(File.ReadAllBytes(copied).SequenceEqual(bytes) && File.ReadAllBytes(source).SequenceEqual(bytes), "Binary GLB copy preserves bytes and source");
        var second = FileInbox.ImportAsync([source]).GetAwaiter().GetResult().Single();
        Check(second != copied && File.Exists(copied) && File.ReadAllBytes(second).SequenceEqual(bytes), "Duplicate filenames never overwrite existing assets");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { FileInbox.ImportAsync([source], cancel.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
        Check(!Directory.EnumerateFiles(FileInbox.Root, ".import-*").Any(), "Cancelled imports leave no partial asset");
        Check(LocalFilesSettingsStore.LoadRules().Any(r => r.Path == FileInbox.Root && r.Read && r.Write && !r.Delete), "Inbox grants read and write without deletion permission");
    }

    internal static void Dashboard(MainForm form)
    {
        Check(Descendants<Button>(form).Count(b => b.Name.StartsWith("rail-stop-")) == 4, "Right rail provides an independent Stop for each MCP service");
        Check(Descendants<ComboBox>(form).Any(c => c.Name == "GodotProjectPicker" && c.Items.Count > 0), "Registered Godot projects appear in selector");
        Check(Descendants<Button>(form).Any(b => b.Name == "AddGodotProject") && Descendants<Button>(form).Any(b => b.Name == "OpenFileInbox"), "Add project and file inbox are reachable");
    }

    internal static void FileWindow(MainForm owner, string fixture)
    {
        using var inbox = new FileInboxForm();
        inbox.Show(owner); Application.DoEvents();
        var wide = inbox.Size;
        inbox.Width = inbox.MinimumSize.Width; Application.DoEvents();
        Check(Descendants<Button>(inbox).All(b => b.Right <= b.Parent!.ClientSize.Width && b.Bottom <= b.Parent.ClientSize.Height), "File actions wrap within the minimum window at native DPI");
        inbox.Size = wide; Application.DoEvents();
        Check(Descendants<Label>(inbox).Any(l => l.Text.StartsWith("Файлов:")), "File window lists existing inbox assets");
        var source = Path.Combine(fixture, "drop.glb");
        File.WriteAllBytes(source, [0x67, 0x6c, 0x54, 0x46, 9, 8, 7]);
        var data = new DataObject(DataFormats.FileDrop, new[] { source });
        var drag = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
        typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(inbox, [drag]);
        Check(drag.Effect == DragDropEffects.Copy, "Inbox accepts Windows file drops as copies");
        typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(inbox, [drag]);
        var destination = Path.Combine(FileInbox.Root, "drop.glb");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(destination) && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(20); }
        Application.DoEvents();
        Check(File.Exists(destination) && File.ReadAllBytes(destination).SequenceEqual(File.ReadAllBytes(source)), "Actual drop handler copies GLB bytes into shared inbox");
        Check(Descendants<ListView>(inbox).Single().Items.Cast<ListViewItem>().Any(i => i.Text == "drop.glb"), "Dropped asset appears in file window");
        var evidence = Environment.GetEnvironmentVariable("HUB_UI_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence);
            using var bitmap = new Bitmap(inbox.Width, inbox.Height);
            inbox.DrawToBitmap(bitmap, new Rectangle(Point.Empty, inbox.Size));
            bitmap.Save(Path.Combine(evidence, "file-inbox.png"));
        }
        inbox.Close();
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed) yield return typed;
            foreach (var match in Descendants<T>(child)) yield return match;
        }
    }

    internal static void WindowBehavior(MainForm form)
    {
        form.WindowState = FormWindowState.Minimized;
        Application.DoEvents();
        Check(form.Visible && form.ShowInTaskbar, "Minimize keeps the ordinary taskbar window");
        form.WindowState = FormWindowState.Normal;
        form.Close();
        Application.DoEvents();
        Check(!form.IsDisposed && !form.Visible, "Close hides to tray without disposing managed services");
        form.RestoreFromTray();
        Check(form.Visible && form.ShowInTaskbar, "Tray restore returns the window");
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
