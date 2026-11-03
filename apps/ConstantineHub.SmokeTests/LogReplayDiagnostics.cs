using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ConstantineHub.Adapters.Godot;

namespace ConstantineHub.SmokeTests;

internal static class LogReplayDiagnostics
{
    internal static void Run(string[] paths)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "hub-log-replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "project.godot"), "[application]\nconfig/name=\"Replay fixture\"\n");
        Environment.SetEnvironmentVariable("CONSTANTINE_GODOT_MCP_CONFIG", Path.Combine(fixture, "godot.json"));
        Environment.SetEnvironmentVariable("CONSTANTINE_FILES_CONFIG", Path.Combine(fixture, "files.json"));
        Environment.SetEnvironmentVariable("CONSTANTINE_HELPER_SETTINGS", Path.Combine(fixture, "helper.json"));
        Environment.SetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_DIR", fixture);
        Environment.SetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_FILE", null);
        File.WriteAllText(Path.Combine(fixture, "files.json"), JsonSerializer.Serialize(new
        {
            schema_version = 1, allowed_roots = new[] { new { path = fixture, permissions = new[] { "read" } } }
        }));
        GodotSettingsStore.Save(new(fixture, "log-replay", "tunnel_replay", 8082, "127.0.0.1", 6262,
            "127.0.0.1", 6263, null, fixture, false));
        using var form = new MainForm(enableTray: false);
        UiPass.Apply(form);
        UiAcceptancePass.Apply(form);
        form.Show();
        Application.DoEvents();
        var log = (RichTextBox)typeof(MainForm).GetField("_log", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var append = typeof(MainForm).GetMethod("Log", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var nativeUpdates = 0;
        log.TextChanged += (_, _) => nativeUpdates++;
        foreach (var hidden in new[] { false, true })
        {
            if (hidden) form.Hide();
            var startCount = nativeUpdates;
            var timer = Stopwatch.StartNew();
            var cpu = Process.GetCurrentProcess().TotalProcessorTime;
            var lines = 0;
            foreach (var path in paths)
                foreach (var line in File.ReadLines(path))
                {
                    append.Invoke(form, [line]);
                    if (++lines % 50 == 0)
                    {
                        Application.DoEvents();
                        form.FlushLogView();
                        if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Replay exceeded 30 seconds.");
                    }
                }
            Application.DoEvents();
            form.FlushLogView();
            Console.WriteLine(JsonSerializer.Serialize(new { hidden, lines, elapsedMs = timer.ElapsedMilliseconds,
                cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds,
                log.TextLength, nativeUpdates = nativeUpdates - startCount }));
        }
        form.RequestExit();
    }

}
