using System.Text.Json.Nodes;
using ConstantineHub.Adapters.Blender;
using ConstantineHub.Adapters.Claudius;
using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

/// <summary>Hermetic Claudius channel checks: never touches the workstation's Claude or Hub config.</summary>
internal static class ClaudiusTests
{
    internal static void Run(string fixture)
    {
        var root = Path.Combine(fixture, "claudius");
        Directory.CreateDirectory(root);
        var claudeConfig = Path.Combine(root, "claude_desktop_config.json");
        Environment.SetEnvironmentVariable("CONSTANTINE_CLAUDIUS_CONFIG", Path.Combine(root, "claudius.json"));
        Environment.SetEnvironmentVariable("CONSTANTINE_CLAUDE_DESKTOP_CONFIG", claudeConfig);
        File.WriteAllText(claudeConfig,
            "{\"preferences\":{\"keep\":true},\"mcpServers\":{\"other\":{\"command\":\"other.exe\"}}}");
        var launcher = Path.Combine(root, "run_claudius_channel.cmd");
        File.WriteAllText(launcher, "@echo off\r\n");
        var server = Path.Combine(root, "run_blender_mcp_server.cmd");
        File.WriteAllText(server, "@echo off\r\n");

        Check(!ClaudiusChannelStore.Load().Enabled, "missing Claudius config is a closed channel");

        var backup = ClaudeDesktopConfig.Register(claudeConfig, launcher, ClaudiusChannelStore.ConfigPath);
        var written = JsonNode.Parse(File.ReadAllText(claudeConfig))!;
        Check(backup is not null && File.Exists(backup), "registration backs up Claude Desktop config first");
        Check(written["preferences"]?["keep"]?.GetValue<bool>() == true &&
              written["mcpServers"]?["other"]?["command"]?.GetValue<string>() == "other.exe",
            "registration preserves every unrelated Claude Desktop setting and server");
        Check(JsonNode.DeepEquals(written["mcpServers"]?[ClaudeDesktopConfig.ServerName],
                ClaudeDesktopConfig.BuildEntry(launcher, ClaudiusChannelStore.ConfigPath)),
            "Claude entry launches the gateway with the ConHub switch path");
        Check(ClaudeDesktopConfig.Register(claudeConfig, launcher, ClaudiusChannelStore.ConfigPath) is null,
            "repeated registration is idempotent and makes no extra backup");
        var inspected = ClaudeDesktopConfig.Inspect(claudeConfig, launcher, ClaudiusChannelStore.ConfigPath);
        Check(inspected.Registered && inspected.Current, "registration inspection reports a current entry");
        Check(!ClaudeDesktopConfig.Inspect(claudeConfig, launcher + ".old", ClaudiusChannelStore.ConfigPath).Current,
            "an entry for another launcher is reported as bypassing the channel");

        ClaudiusChannelStore.Save(ClaudiusSettings.Default with { ServerCommand = server });
        var adapter = new ClaudiusAdapter(() => BlenderSettings.Default with { TunnelProfile = "claudius-missing-profile" })
            { Launcher = launcher };
        var closed = adapter.GetStatusAsync().GetAwaiter().GetResult();
        Check(closed.State == AdapterState.Stopped, "registered closed channel reports Stopped: " + closed.Summary);
        adapter.StartAsync().GetAwaiter().GetResult();
        var opened = adapter.GetStatusAsync().GetAwaiter().GetResult();
        Check(ClaudiusChannelStore.Load() is { Enabled: true } s && s.ServerCommand == server,
            "Start opens the switch and records the Blender MCP server command");
        Check(opened.State == AdapterState.Running && opened.Summary.Contains("waiting", StringComparison.Ordinal),
            "open channel without a gateway waits for Claude Desktop instead of claiming a connection");
        adapter.StopAsync().GetAwaiter().GetResult();
        Check(ClaudiusChannelStore.Load() is { Enabled: false } c && c.ServerCommand == server,
            "Stop only closes the switch and keeps the server command");

        var now = DateTimeOffset.UtcNow;
        var live = new ClaudiusGatewayState(Environment.ProcessId, null, now, now, null, "claude", 1, 0, now, "list_skills");
        Check(ClaudiusChannelStore.IsLive(live, now), "fresh heartbeat from a running process is live");
        Check(!ClaudiusChannelStore.IsLive(live with { StoppedUtc = now }, now), "stop marker means not live");
        Check(!ClaudiusChannelStore.IsLive(live, now.AddMinutes(2)), "stale heartbeat means not live");

        File.WriteAllText(ClaudiusChannelStore.ConfigPath, "{broken");
        Check(adapter.GetStatusAsync().GetAwaiter().GetResult().State == AdapterState.Failed,
            "unreadable channel config is reported, not silently treated as open");
        ClaudiusChannelStore.Save(ClaudiusSettings.Default);
    }

    internal static void Ui(MainForm form)
    {
        Control? Host(string title) => Descendants<Label>(form).Where(l => l.Text == title)
            .Select(FindDashboardHost).Distinct().SingleOrDefault();
        var claudius = Host(MainForm.ClaudiusTitle);
        var local = Host("LOCAL FILES MCP");
        Check(claudius is not null && local is not null && claudius.Parent == local.Parent,
            "Claudius and Local Files cards are on the dashboard");
        var flow = (FlowLayoutPanel)claudius!.Parent!;
        Check(flow.Controls.GetChildIndex(claudius) + 1 == flow.Controls.GetChildIndex(local!),
            "Claudius card sits directly above Local Files MCP");
        Check(Descendants<PictureBox>(claudius).Any(p => p.Image is not null), "Claudius card uses the supplied icon");
        Check(Descendants<Button>(form).Count(b => b.Name == "rail-stop-claudius") == 1 &&
              Descendants<Label>(form).Any(l => l.Name == "rail-title-claudius" && l.Image is not null),
            "Claudius appears with its icon in Active MCP Bridges");
        Check(MainForm.LoadClaudiusIcon() is not null, "Claudius .ico ships with the build for dialogs");
    }

    private static Control? FindDashboardHost(Control control)
    {
        for (var current = control; current is not null; current = current.Parent)
            if (current.Parent is FlowLayoutPanel) return current;
        return null;
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
