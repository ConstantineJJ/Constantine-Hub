using ConstantineHub.Adapters.Nvidia;
using ConstantineHub.Core;

namespace ConstantineHub.SmokeTests;

internal static class NvidiaUiTests
{
    internal static void Run(MainForm form)
    {
        Endpoints(); BoundedReplies(); ProfileScalars(); Layout(form);
        var original = form.Size;
        form.Size = form.MinimumSize; Application.DoEvents(); Layout(form);
        var reconnect = Descendants<ReconnectButton>(form).Single();
        foreach (var state in new[] { ReconnectState.Observing, ReconnectState.Recovering, ReconnectState.Waiting })
        {
            reconnect.SetState(true, state, state == ReconnectState.Waiting ? 9 : 0);
            Application.DoEvents(); Capture(form, state.ToString());
        }
        reconnect.SetState(false, ReconnectState.Observing, 0);
        form.Size = original; Application.DoEvents();
    }

    internal static async Task Live()
    {
        Endpoints(); BoundedReplies(); ProfileScalars();
        using var adapter = new NvidiaAppAdapter();
        var snapshot = await adapter.GetSnapshotAsync();
        Check(snapshot.HttpAvailable && snapshot.ToolCount > 0 && snapshot.StdioBridgeInstalled,
            $"Actual NVIDIA loopback MCP initialized and enumerated {snapshot.ToolCount} tools; installed relay detected");
        Console.WriteLine(snapshot.HttpMessage);
        Console.WriteLine("Tunnel: " + snapshot.TunnelStatus.Summary);
    }

    private static void BoundedReplies()
    {
        using var valid = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.StringContent("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[]}}") };
        using var parsed = NvidiaAppAdapter.ReadReplyAsync(valid, CancellationToken.None).GetAwaiter().GetResult();
        Check(parsed.RootElement.GetProperty("id").GetInt32() == 2, "NVIDIA diagnostic JSON response remains valid");
        using var sse = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.StringContent("event: message\r\ndata: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{}}\r\n\r\n",
                System.Text.Encoding.UTF8, "text/event-stream") };
        using var eventReply = NvidiaAppAdapter.ReadReplyAsync(sse, CancellationToken.None).GetAwaiter().GetResult();
        Check(eventReply.RootElement.GetProperty("id").GetInt32() == 2, "NVIDIA diagnostic accepts Streamable HTTP SSE reply events");
        using var interleaved = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.StringContent("data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n" +
                "data: {\"jsonrpc\":\"2.0\",\"id\":99,\"result\":{}}\n\n" +
                "data: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[]}}\n\n",
                System.Text.Encoding.UTF8, "text/event-stream") };
        using var matched = NvidiaAppAdapter.ReadReplyAsync(interleaved, CancellationToken.None).GetAwaiter().GetResult();
        Check(matched.RootElement.GetProperty("id").GetInt32() == 2,
            "NVIDIA diagnostic skips SSE notifications and unrelated response IDs before matching reply");
        foreach (var mediaType in new[] { "application/json", "text/event-stream" })
        {
            var payload = "{\"jsonrpc\":\"2.0\",\"id\":99,\"result\":{}}";
            if (mediaType == "text/event-stream") payload = "data: " + payload + "\n\n";
            using var wrongId = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, mediaType) };
            try { using var wrong = NvidiaAppAdapter.ReadReplyAsync(wrongId, CancellationToken.None).GetAwaiter().GetResult(); throw new InvalidOperationException("Wrong response ID accepted"); }
            catch (System.Text.Json.JsonException) { Check(true, "NVIDIA diagnostic rejects unmatched response ID in " + mediaType); }
        }
        using var oversized = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.StringContent(new string('x', 512 * 1024 + 1)) };
        try { NvidiaAppAdapter.ReadReplyAsync(oversized, CancellationToken.None).GetAwaiter().GetResult(); throw new InvalidOperationException("Oversize accepted"); }
        catch (IOException) { Check(true, "NVIDIA diagnostic rejects responses over 512 KiB"); }
    }

    private static void ProfileScalars()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "conhub-nvidia-yaml-" + Guid.NewGuid().ToString("N") + ".yaml");
        var oldFile = Environment.GetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_FILE");
        var relay = NvidiaAppAdapter.BridgeExecutable.Replace('\\', '/');
        var quotedRelay = '"' + relay + '"';
        try
        {
            Environment.SetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_FILE", fixture);
            foreach (var (scalar, expected) in new[] {
                (System.Text.Json.JsonSerializer.Serialize(quotedRelay, new System.Text.Json.JsonSerializerOptions {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), quotedRelay),
                (System.Text.Json.JsonSerializer.Serialize(quotedRelay), quotedRelay),
                (System.Text.Json.JsonSerializer.Serialize(NvidiaAppAdapter.BridgeExecutable), NvidiaAppAdapter.BridgeExecutable),
                ("'" + relay + "'", relay), (relay, relay),
                ("'C:/Agent''s folder/relay.exe'", "C:/Agent's folder/relay.exe"),
                ("'C:/Agent#1/relay.exe'", "C:/Agent#1/relay.exe"),
                ("C:/Agent#1/relay.exe # trailing comment", "C:/Agent#1/relay.exe") })
            {
                File.WriteAllText(fixture, "tunnel_id: 'fixture_id' # retained comment\r\nchannels:\r\n  main:\r\n    command: " + scalar +
                    "\r\nhealth:\r\n  listen_addr: \"127.0.0.1:8083\" # listener\r\n");
                var profile = TunnelProfile.Load("nvidia-fixture");
                Check(profile.McpCommand == expected && profile.TunnelId == "fixture_id" && profile.HealthPort == 8083,
                    "NVIDIA tunnel profile preserves native or legacy YAML scalar: " + scalar);
            }
            foreach (var command in new[] { relay, quotedRelay, NvidiaAppAdapter.BridgeExecutable })
                Check(NvidiaAppAdapter.IsInstalledRelayCommand(command), "NVIDIA accepts its exact installed relay with optional executable quotes");
            foreach (var command in new[] { quotedRelay + " --extra", relay + " --extra", quotedRelay + " & calc.exe", "C:/other/NvAppMcpServer.exe" })
                Check(!NvidiaAppAdapter.IsInstalledRelayCommand(command), "NVIDIA rejects another executable or relay arguments");
            foreach (var scalar in new[] { "\"unterminated", "'unterminated", "\"valid\" unexpected", "'valid' unexpected" })
            {
                File.WriteAllText(fixture, "tunnel_id: fixture\ncommand: " + scalar + "\nlisten_addr: 127.0.0.1:8083\n");
                try { TunnelProfile.Load("nvidia-fixture"); throw new InvalidOperationException("Malformed YAML scalar accepted"); }
                catch (InvalidDataException) { Check(true, "Malformed quoted tunnel profile fails explicitly"); }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TUNNEL_CLIENT_PROFILE_FILE", oldFile);
            File.Delete(fixture);
        }
    }
    internal static void Endpoints()
    {
        Check(NvidiaAppAdapter.GetLocalEndpoint("{\"http\":\"http://127.0.0.1:13509/mcp\"}").Port == 13509,
            "NVIDIA discovery permits only the installed loopback MCP endpoint");
        foreach (var value in new[] { "http://0.0.0.0:13509/mcp", "http://example.com/mcp", "https://127.0.0.1:13509/mcp",
            "http://127.0.0.1:13509/other", "http://user@127.0.0.1:13509/mcp", "http://127.0.0.1:13509/mcp?token=x", "invalid" })
            Check(NvidiaAppAdapter.GetLocalEndpoint(System.Text.Json.JsonSerializer.Serialize(new { http = value })).Port == 13508,
                "NVIDIA rejects unsafe discovery endpoint: " + value);
        Check(NvidiaAppAdapter.GetLocalEndpoint("{invalid").Port == 13508, "Malformed NVIDIA discovery falls back safely");
    }

    internal static void Layout(MainForm form)
    {
        var shell = form.Controls.OfType<TableLayoutPanel>().First(p => p.ColumnCount == 3);
        var nav = shell.GetControlFromPosition(0, 0)!;
        var top = shell.GetControlFromPosition(1, 0)!;
        var main = shell.GetControlFromPosition(1, 1)!;
        Check(Descendants<ConHubBrand>(nav).Single().Text == "ConHub.", "ConHub. appears on one line below the logo");
        Check(ConHubBrand.ConColor.ToArgb() == Color.FromArgb(252, 96, 100).ToArgb() &&
            ConHubBrand.HubColor.ToArgb() == Color.FromArgb(191, 252, 164).ToArgb(), "ConHub. brand uses exact requested colors");
        Check(!Descendants<Label>(form).Any(l => l.Text == "Dashboard"), "Dashboard label removed");
        Check(Descendants<Button>(top).Any(b => b.Text == "Home") && !Descendants<Button>(nav).Any(b => b.Text == "Home"),
            "Home navigation moved into top bar");
        Check(Descendants<ComboBox>(top).Any(p => p.Name == "GodotProjectPicker"), "Real Godot project selector moved to top bar");
        Check(Descendants<Panel>(nav).Count(p => p.Name == "NvidiaUtilityCard") == 1 &&
            !Descendants<Panel>(main).Any(p => p.Name == "NvidiaUtilityCard"), "NVIDIA has one Utility card in the sidebar");
        var art = Descendants<PictureBox>(nav).Single(p => p.Name == "NvidiaJensenArt");
        Check(art.SizeMode == PictureBoxSizeMode.Zoom, "Jensen artwork preserves its aspect ratio");
        foreach (var button in Descendants<Button>(nav).Where(b => b.Name.StartsWith("nvidia-")))
        {
            var size = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Check(size.Width + 6 <= button.ClientSize.Width && size.Height + 4 <= button.ClientSize.Height,
                "NVIDIA button caption fits at native DPI: " + button.Text);
        }
        var layout = (TableLayoutPanel)art.Parent!;
        Check(layout.RowStyles[0].Height == 60 && layout.RowStyles[1].Height == 40, "NVIDIA lower text area is two thirds of art height");
        Check(Descendants<Button>(form).Count(b => b.Name == "rail-stop-nvidia-app") == 1, "NVIDIA appears in active MCP bridges");
        var reconnect = Descendants<ReconnectButton>(main).Single();
        Check(reconnect.Visible && reconnect.Parent is TableLayoutPanel && reconnect.Right <= reconnect.Parent.ClientSize.Width,
            "ReConnect stays visible in the collapsed Godot header");
        var logo = Descendants<PictureBox>(nav).Single(p => p.Name == "ConHubLogo");
        Check(logo.Height >= 156 * form.DeviceDpi / 96, "Logo is at least three times its previous 52px size");
        foreach (var control in Descendants<Button>(top).Cast<Control>().Concat(Descendants<ComboBox>(top)))
            Check(control.Right <= control.Parent!.ClientSize.Width && control.Bottom <= control.Parent.ClientSize.Height,
                "Top action fits: " + control.Name + " " + control.Text);
        Capture(form, "off");
    }

    private static void Capture(MainForm form, string state)
    {
        var evidence = Environment.GetEnvironmentVariable("HUB_UI_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(evidence, $"conhub-v054-{form.Width}x{form.Height}-{state}.png"));
        }
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}
