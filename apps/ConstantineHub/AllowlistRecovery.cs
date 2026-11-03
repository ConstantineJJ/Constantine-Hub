using ConstantineHub.Adapters.LocalFiles;

namespace ConstantineHub;

internal sealed partial class MainForm
{
    private TableLayoutPanel? _allowlistNotice;
    private Label? _allowlistNoticeText;
    private Button? _removeUnavailableButton;
    private string? _dismissedAllowlistIssue;
    private string? _currentAllowlistIssue;

    private void AttachAllowlistRecovery()
    {
        int S(int value) => (int)Math.Round(value * Math.Max(1f, DeviceDpi / 96f));
        var shell = Controls.OfType<TableLayoutPanel>().First(p => p.ColumnCount == 3);
        var dashboard = (FlowLayoutPanel)shell.GetControlFromPosition(1, 1)!;
        _allowlistNotice = new TableLayoutPanel
        {
            Name = "AllowlistRecoveryNotice", ColumnCount = 1, RowCount = 2,
            Height = S(134), Padding = new Padding(S(12)), Margin = new Padding(0, 0, 0, S(10)),
            BackColor = Color.FromArgb(51, 41, 25), Visible = false
        };
        _allowlistNotice.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _allowlistNotice.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _allowlistNotice.RowStyles.Add(new RowStyle(SizeType.Absolute, S(42)));
        _allowlistNoticeText = new Label
        {
            Name = "AllowlistRecoveryMessage", Dock = DockStyle.Fill,
            ForeColor = Warn, AutoEllipsis = true, Font = new Font("Segoe UI", 10F)
        };
        _allowlistNotice.Controls.Add(_allowlistNoticeText, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _removeUnavailableButton = MakeButton("Удалить недоступные пути", async (_, _) => await RemoveUnavailablePathsAsync());
        _removeUnavailableButton.Name = "RemoveUnavailableAllowlistPaths";
        var configure = MakeButton("Настроить пути…", async (_, _) => await OpenLocalFilesSettingsAsync());
        configure.Name = "ConfigureUnavailableAllowlistPaths";
        var later = MakeButton("Позже", (_, _) =>
        {
            _dismissedAllowlistIssue = _currentAllowlistIssue;
            _allowlistNotice.Visible = false;
        });
        later.Name = "DismissAllowlistNotice";
        foreach (var (button, width) in new[] { (_removeUnavailableButton, 224), (configure, 176), (later, 76) })
        {
            button.Size = new Size(S(width), S(34));
            button.Margin = new Padding(0, 0, S(6), 0);
            button.Font = new Font("Segoe UI", 9F);
            actions.Controls.Add(button);
        }
        _allowlistNotice.Controls.Add(actions, 0, 1);
        dashboard.Controls.Add(_allowlistNotice);
        dashboard.Controls.SetChildIndex(_allowlistNotice, 0);
        RefreshAllowlistNotification();
    }

    internal void RefreshAllowlistNotification()
    {
        if (_allowlistNotice is null) return;
        try
        {
            var rules = LocalFilesSettingsStore.LoadRules(requireAvailable: false);
            var unavailable = LocalFilesSettingsStore.UnavailablePaths(rules);
            _currentAllowlistIssue = rules.Count == 0 ? "empty" : string.Join("\n", unavailable.Order(StringComparer.OrdinalIgnoreCase));
            if (unavailable.Count == 0 && rules.Count > 0)
            { _dismissedAllowlistIssue = null; _allowlistNotice.Visible = false; return; }
            if (_currentAllowlistIssue == _dismissedAllowlistIssue)
            { _allowlistNotice.Visible = false; return; }
            _allowlistNoticeText!.Text = rules.Count == 0
                ? "Allowlist пуст. Добавьте папку в настройках, чтобы подключить Local Files."
                : $"Недоступные папки в Allowlist: {unavailable.Count}\n" + string.Join("; ", unavailable);
            _tunnelActionTips.SetToolTip(_allowlistNoticeText, string.Join(Environment.NewLine, unavailable));
            _removeUnavailableButton!.Enabled = unavailable.Count > 0;
            _allowlistNotice.Visible = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or
            ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Structural config errors are reported by the existing status card;
            // never offer to discard records that could not be parsed safely.
            _allowlistNotice.Visible = false;
        }
    }

    internal async Task RemoveUnavailablePathsAsync()
    {
        await RunAdapterActionAsync("Repair Local Files allowlist", async _ =>
        {
            var removed = LocalFilesSettingsStore.RemoveUnavailableRoots();
            Log($"Removed {removed} unavailable allowlist entries. Files and folders were left untouched.");
            await ApplyFileAccessAsync(removed > 0);
        });
        _dismissedAllowlistIssue = null;
        await RefreshStatusAsync();
    }
}
