namespace ConstantineHub.Adapters.LocalFiles;

internal sealed class LocalFilesSettingsForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly Label _configPath = new();
    private readonly Label _hint = new();

    internal LocalFilesSettingsForm()
    {
        Text = "Local Files MCP — Allowed Folders";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 560);
        Size = new Size(1080, 660);
        BackColor = Color.FromArgb(22, 24, 29);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = ConstantineHub.Core.AppIconProvider.Current;

        BuildUi();
        LoadExistingRules();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            RowCount = 5,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        Controls.Add(root);

        var header = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Allowed folders",
            Font = new Font("Segoe UI Semibold", 18F),
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(header, 0, 0);

        _configPath.Dock = DockStyle.Fill;
        _configPath.ForeColor = Color.FromArgb(170, 174, 184);
        _configPath.Text = "Config: " + LocalFilesSettingsStore.ConfigPath;
        _configPath.TextAlign = ContentAlignment.MiddleLeft;
        _configPath.AutoEllipsis = true;
        root.Controls.Add(_configPath, 0, 1);

        ConfigureGrid();
        root.Controls.Add(_grid, 0, 2);

        _hint.Dock = DockStyle.Fill;
        _hint.ForeColor = Color.FromArgb(200, 176, 112);
        _hint.TextAlign = ContentAlignment.MiddleLeft;
        _hint.Text = "Read is safest. Write allows changes. Delete allows destructive operations. Nested roots are allowed, but overlapping permissions are additive.";
        root.Controls.Add(_hint, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = true
        };

        buttons.Controls.Add(MakeButton("Cancel", (_, _) => Close()));
        buttons.Controls.Add(MakeButton("Save", (_, _) => SaveRules(), primary: true));
        buttons.Controls.Add(MakeButton("Remove", (_, _) => RemoveSelected()));
        buttons.Controls.Add(MakeButton("Add Folder", (_, _) => AddFolder()));
        buttons.Controls.Add(MakeButton("Новое место…", (_, _) => RelocateSelected()));
        var removeUnavailable = MakeButton("Удалить недоступные", (_, _) => RemoveUnavailableRows());
        removeUnavailable.Name = "RemoveUnavailableAllowlistRows";
        removeUnavailable.MinimumSize = new Size(190, 38);
        buttons.Controls.Add(removeUnavailable);
        root.Controls.Add(buttons, 0, 4);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.BackgroundColor = Color.FromArgb(15, 17, 21);
        _grid.GridColor = Color.FromArgb(58, 62, 72);
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 38, 46);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.Gainsboro;
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(25, 28, 34);
        _grid.DefaultCellStyle.ForeColor = Color.Gainsboro;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(55, 82, 120);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Path",
            HeaderText = "Папка (можно изменить путь)",
            DataPropertyName = "Path",
            ReadOnly = false,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 420
        });
        _grid.Columns.Add(MakeCheckColumn("Read", "Read", 80));
        _grid.Columns.Add(MakeCheckColumn("Write", "Write", 80));
        _grid.Columns.Add(MakeCheckColumn("Delete", "Delete", 85));
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Availability", HeaderText = "Состояние", ReadOnly = true, Width = 130
        });
        _grid.CellEndEdit += (_, _) => RefreshAvailability();
    }

    private static DataGridViewCheckBoxColumn MakeCheckColumn(string name, string text, int width)
        => new()
        {
            Name = name,
            HeaderText = text,
            Width = width,
            FlatStyle = FlatStyle.Standard,
            ThreeState = false
        };

    private static Button MakeButton(string text, EventHandler handler, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(120, 38),
            Width = 120,
            Height = 38,
            Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(55, 106, 175) : Color.FromArgb(42, 46, 55),
            ForeColor = Color.White
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 74, 85);
        button.Click += handler;
        return button;
    }

    private void LoadExistingRules()
    {
        try
        {
            foreach (var rule in LocalFilesSettingsStore.LoadRules(requireAvailable: false))
                AddRow(rule);
            RefreshAvailability();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Local Files MCP config error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void AddFolder()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = "Choose a folder ChatGPT may access through Local Files MCP",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var canonical = LocalFilesSettingsStore.NormalizeExistingDirectory(picker.SelectedPath);
            if (CurrentRules().Any(rule => string.Equals(rule.Path, canonical, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "That canonical folder is already in the allowlist.", "Duplicate folder");
                return;
            }

            AddRow(new LocalFilesRootRule(canonical, Read: true, Write: false, Delete: false));
            RefreshAvailability();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot add folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RemoveSelected()
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        _grid.Rows.RemoveAt(_grid.SelectedRows[0].Index);
        RefreshAvailability();
    }

    private void RelocateSelected()
    {
        if (_grid.SelectedRows.Count == 0) return;
        using var picker = new FolderBrowserDialog
        {
            Description = "Укажите новое расположение выбранной папки",
            UseDescriptionForTitle = true, ShowNewFolderButton = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { ReplaceFolder(_grid.SelectedRows[0].Index, picker.SelectedPath); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Новое расположение", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    internal void ReplaceFolder(int rowIndex, string folder)
    {
        _grid.EndEdit();
        var canonical = LocalFilesSettingsStore.NormalizeExistingDirectory(folder);
        if (_grid.Rows.Cast<DataGridViewRow>().Any(row => row.Index != rowIndex &&
            string.Equals(row.Cells["Path"].Value?.ToString(), canonical, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("That canonical folder is already in the allowlist.");
        // Keep the original read/write/delete choices when a folder moves.
        _grid.Rows[rowIndex].Cells["Path"].Value = canonical;
        RefreshAvailability();
    }

    internal int RemoveUnavailableRows()
    {
        _grid.EndEdit();
        var missing = _grid.Rows.Cast<DataGridViewRow>()
            .Where(row => !Directory.Exists(row.Cells["Path"].Value?.ToString())).ToArray();
        foreach (var row in missing) _grid.Rows.Remove(row);
        RefreshAvailability();
        return missing.Length;
    }

    private void RefreshAvailability()
    {
        var unavailable = 0;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var exists = Directory.Exists(row.Cells["Path"].Value?.ToString());
            row.Cells["Availability"].Value = exists ? "Доступна" : "Недоступна";
            row.Cells["Availability"].Style.ForeColor = exists ? Color.LightGreen : Color.FromArgb(232, 188, 104);
            if (!exists) unavailable++;
        }
        _hint.Text = unavailable > 0
            ? $"Недоступных папок: {unavailable}. Выберите строку и нажмите «Новое место…» либо удалите запись. Изменения применяются после Save."
            : "Read is safest. Write allows changes. Delete allows destructive operations. Overlapping permissions are additive.";
    }

    private void SaveRules()
    {
        _grid.EndEdit();
        IReadOnlyList<LocalFilesRootRule> rules;
        IReadOnlyList<string> warnings;
        try
        {
            rules = CurrentRules().ToList();
            warnings = LocalFilesSettingsStore.ValidateRules(rules, requireAvailable: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid allowlist", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (warnings.Count > 0)
        {
            var warningText = string.Join(Environment.NewLine + Environment.NewLine, warnings);
            if (MessageBox.Show(
                    this,
                    warningText + Environment.NewLine + Environment.NewLine + "Save this configuration anyway?",
                    "Nested folder warning",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
        }

        if (rules.Any(rule => rule.Delete))
        {
            if (MessageBox.Show(
                    this,
                    "Delete permission is enabled for one or more roots. This allows destructive file operations inside those roots. Continue?",
                    "Confirm delete access",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
        }

        try
        {
            LocalFilesSettingsStore.SaveRules(rules, requireAvailable: false);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private IEnumerable<LocalFilesRootRule> CurrentRules()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var path = row.Cells["Path"].Value?.ToString() ?? string.Empty;
            yield return new LocalFilesRootRule(
                path,
                Read: Convert.ToBoolean(row.Cells["Read"].Value ?? false),
                Write: Convert.ToBoolean(row.Cells["Write"].Value ?? false),
                Delete: Convert.ToBoolean(row.Cells["Delete"].Value ?? false));
        }
    }

    private void AddRow(LocalFilesRootRule rule)
    {
        var index = _grid.Rows.Add();
        var row = _grid.Rows[index];
        row.Cells["Path"].Value = rule.Path;
        row.Cells["Read"].Value = rule.Read;
        row.Cells["Write"].Value = rule.Write;
        row.Cells["Delete"].Value = rule.Delete;
    }
}
