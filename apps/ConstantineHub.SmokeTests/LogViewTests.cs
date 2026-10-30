using System.Reflection;

namespace ConstantineHub.SmokeTests;

internal static class LogViewTests
{
    internal static void Run()
    {
        using var form = new MainForm(enableTray: false);
        UiPass.Apply(form);
        UiAcceptancePass.Apply(form);
        form.Show();
        Application.DoEvents();
        var log = (RichTextBox)typeof(MainForm).GetField("_log", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var append = typeof(MainForm).GetMethod("Log", BindingFlags.Instance | BindingFlags.NonPublic)!;
        append.Invoke(form, ["first " + new string('a', 150000)]);
        append.Invoke(form, ["latest " + new string('b', 150000)]);
        form.FlushLogView();
        Check(log.ReadOnly && log.TextLength <= 250000 && log.Text.Contains("latest "),
            "read-only diagnostics really discard old text and retain the latest message within 250000 characters");
        var beforeBatch = log.Text;
        var updates = 0;
        log.TextChanged += (_, _) => updates++;
        for (var i = 0; i < 100; i++) append.Invoke(form, ["batched line " + i]);
        Check(log.Text == beforeBatch && updates == 0, "incoming log lines do not redraw RichEdit individually");
        form.FlushLogView();
        Check(updates == 1 && log.Text.Contains("batched line 99"), "a log batch is rendered with one native text update");
        form.Hide();
        var hiddenText = log.Text;
        updates = 0;
        for (var i = 0; i < 3200; i++) append.Invoke(form, ["hidden line " + i]);
        form.FlushLogView();
        var history = (List<string>)typeof(MainForm).GetField("_allLogLines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        Check(log.Text == hiddenText && updates == 0 && history.Count <= 3000 && history[^1].Contains("hidden line 3199"),
            "tray logging keeps bounded recent diagnostics without touching the native text control");
        form.RestoreFromTray();
        form.FlushLogView();
        Check(log.Text.Contains("hidden line 3199") && log.TextLength <= MainForm.LogViewCharacterLimit,
            "restoring Hub displays recent tray logs within the character limit");
        log.Visible = false;
        var collapsedText = log.Text;
        append.Invoke(form, ["collapsed log message"]);
        form.FlushLogView();
        Check(log.Text == collapsedText, "collapsed diagnostics are not redrawn in the background");
        log.Visible = true;
        form.FlushLogView();
        Check(log.Text.Contains("collapsed log message"), "expanding diagnostics refreshes the pending snapshot");
        append.Invoke(form, ["{\"time\":\"fixture\",\"level\":\"INFO\",\"msg\":\"verbose-fixture\"}"]);
        form.FlushLogView();
        Check(!log.Text.Contains("verbose-fixture"), "routine verbose traffic remains hidden in normal mode");
        typeof(MainForm).GetMethod("ToggleVerboseLogs", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        Check(log.Text.Contains("verbose-fixture") && log.TextLength <= MainForm.LogViewCharacterLimit,
            "verbose rebuild also respects the native view limit");
        append.Invoke(form, ["long tail " + new string('c', 300000) + " preserved-end"]);
        form.FlushLogView();
        Check(log.ReadOnly && log.TextLength <= MainForm.LogViewCharacterLimit && log.Text.EndsWith("preserved-end"),
            "a single oversized message cannot bypass the display bound or remove its latest tail");
        form.RequestExit();
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
