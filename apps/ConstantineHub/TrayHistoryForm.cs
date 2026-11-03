using ConstantineHub.Core;

namespace ConstantineHub;

internal sealed class TrayHistoryForm : Form
{
    private readonly ListView _entries = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private readonly Button _delete = new() { Text = "Delete selected legacy entry…", AutoSize = true, Enabled = false };
    private readonly Label _details = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };

    internal TrayHistoryForm()
    {
        Text = "ConHub. — notification icon history";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 460);
        Size = new Size(1100, 600);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(1000, 0), Padding = new Padding(0, 0, 0, 10),
            Text = "Preview of Windows 11 NotifyIconSettings. Only inactive legacy records at the exact current installation path can be removed. " +
                "Current identity, other applications and unverified historical copies are protected. Windows may keep other historical records; no global cache reset is performed."
        }, 0, 0);
        _entries.Columns.Add("Entry", 120);
        _entries.Columns.Add("Executable", 390);
        _entries.Columns.Add("Tooltip", 160);
        _entries.Columns.Add("Assessment", 380);
        layout.Controls.Add(_entries, 0, 1);
        layout.Controls.Add(_details, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var refresh = new Button { Text = "Refresh preview", AutoSize = true };
        refresh.Click += (_, _) => RefreshPreview();
        actions.Controls.Add(refresh);
        actions.Controls.Add(_delete);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(close);
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);
        CancelButton = close;
        _entries.SelectedIndexChanged += (_, _) =>
        {
            var entry = Selected();
            _delete.Enabled = entry?.CanDelete == true;
            _details.Text = entry is null ? string.Empty : $"{entry.KeyName}: {entry.Reason}";
        };
        _delete.Click += (_, _) => DeleteSelected();
        Shown += (_, _) => RefreshPreview();
    }

    private TrayHistoryEntry? Selected() => _entries.SelectedItems.Count == 1
        ? _entries.SelectedItems[0].Tag as TrayHistoryEntry : null;

    private void RefreshPreview()
    {
        _delete.Enabled = false;
        _entries.Items.Clear();
        try
        {
            foreach (var entry in TrayHistory.Preview())
                _entries.Items.Add(new ListViewItem([entry.KeyName, entry.ExecutablePath, entry.Tooltip, entry.Reason]) { Tag = entry });
            _details.Text = $"{_entries.Items.Count} related history entries. Preview does not change Windows settings.";
        }
        catch (Exception ex) { _details.Text = "History preview unavailable: " + ex.Message; }
    }

    private void DeleteSelected()
    {
        if (Selected() is not { CanDelete: true } entry) return;
        if (MessageBox.Show(this,
            $"Delete exactly this inactive legacy notification entry?\n\nHKCU\\{TrayHistory.RegistryPath}\\{entry.KeyName}\n{entry.ExecutablePath}\n{entry.Tooltip}\n\nA typed JSON backup will be saved before deletion. Other entries and notification preferences remain untouched.",
            "ConHub. — confirm selected history deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            var backup = TrayHistory.DeleteConfirmed(entry);
            RefreshPreview();
            _details.Text = "Selected entry deleted. Backup: " + backup;
        }
        catch (Exception ex) { _details.Text = "Deletion stopped: " + ex.Message; }
    }
}
