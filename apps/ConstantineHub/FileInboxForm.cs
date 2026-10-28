using System.Diagnostics;
using ConstantineHub.Core;

namespace ConstantineHub;

internal sealed class FileInboxForm : Form
{
    private readonly ListView _files = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 2000 };
    private readonly Button _add;
    private bool _importing;
    private string _signature = "\0";

    internal FileInboxForm(string accessStatus = "Доступ агента через Local Files MCP.", bool agentConnected = true)
    {
        Text = "Constantine Hub — Files / Файлы"; Icon = AppIconProvider.Current;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10F);
        StartPosition = FormStartPosition.CenterParent; Size = new Size(860, 560); MinimumSize = new Size(600, 380);
        BackColor = Color.FromArgb(18, 26, 34); ForeColor = Color.Gainsboro;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 86, 34, 36 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var drop = new Label { Text = "Перетащите файлы сюда\nОригиналы сохраняются. Агент получает доступ к копиям в этой папке.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(31, 43, 55), Padding = new Padding(8), AllowDrop = true };
        root.Controls.Add(drop, 0, 0);
        root.Controls.Add(new Label { Text = FileInbox.Root, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Silver }, 0, 1);
        root.Controls.Add(new Label { Text = accessStatus, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = agentConnected ? Color.LightGreen : Color.Khaki, Font = new Font("Segoe UI", 9F) }, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        Button Action(string text, EventHandler action)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(31, 43, 55), ForeColor = Color.White, Padding = new Padding(4), Margin = new Padding(0, 0, 8, 0) };
            button.Click += action; actions.Controls.Add(button); return button;
        }
        _add = Action("Добавить…", async (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "Add files to agent inbox", Multiselect = true, Filter = "All files|*.*" };
            if (picker.ShowDialog(this) == DialogResult.OK) await ImportAsync(picker.FileNames);
        });
        Action("Открыть папку", (_, _) => Process.Start(new ProcessStartInfo(FileInbox.Root) { UseShellExecute = true }));
        Action("Копировать путь", (_, _) => CopySelection(false));
        Action("Запрос для агента", (_, _) => CopySelection(true));
        root.Controls.Add(actions, 0, 3);
        _files.Columns.Add("Файл", 390); _files.Columns.Add("Размер", 120); _files.Columns.Add("Изменён", 180);
        _files.BackColor = Color.FromArgb(12, 18, 24); _files.ForeColor = Color.Gainsboro; _files.AllowDrop = true;
        root.Controls.Add(_files, 0, 4); root.Controls.Add(_status, 0, 5); Controls.Add(root);
        AllowDrop = true;
        foreach (var target in new Control[] { this, drop, _files })
        {
            target.DragEnter += (_, e) => e.Effect = !_importing && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
            target.DragDrop += async (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) await ImportAsync(paths); };
        }
        _refresh.Tick += (_, _) => { if (!_importing) RefreshFiles(); };
        _refresh.Start(); RefreshFiles();
        FormClosing += (_, e) => { if (_importing) { e.Cancel = true; _status.Text = "Дождитесь завершения копирования."; } };
    }

    internal async Task ImportAsync(IEnumerable<string> paths)
    {
        if (_importing) return;
        _importing = true; _add.Enabled = false; _status.Text = "Копирование файлов…";
        try
        {
            var added = await FileInbox.ImportAsync(paths);
            RefreshFiles();
            foreach (ListViewItem item in _files.Items) item.Selected = added.Contains((string)item.Tag!, StringComparer.OrdinalIgnoreCase);
            _status.Text = $"Добавлено: {added.Count}. Скажите агенту, какой файл использовать и что с ним сделать.";
        }
        catch (Exception ex) { _status.Text = "Не удалось добавить файл: " + ex.Message; }
        finally { _importing = false; _add.Enabled = true; }
    }

    private void RefreshFiles()
    {
        try
        {
            Directory.CreateDirectory(FileInbox.Root);
            var files = new DirectoryInfo(FileInbox.Root).EnumerateFiles().Where(f => !f.Name.StartsWith(".import-", StringComparison.Ordinal))
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(500).ToArray();
            var signature = string.Join("|", files.Select(f => f.Name + f.Length + f.LastWriteTimeUtc.Ticks));
            if (signature == _signature) return;
            var selected = _files.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _signature = signature; _files.BeginUpdate(); _files.Items.Clear();
            foreach (var file in files)
            {
                var item = new ListViewItem([file.Name, $"{file.Length / 1024d:N1} KB", file.LastWriteTime.ToString("dd.MM.yyyy HH:mm")]) { Tag = file.FullName, Selected = selected.Contains(file.FullName) };
                _files.Items.Add(item);
            }
            _files.EndUpdate();
            _status.Text = files.Length == 0 ? "Папка пуста. Перетащите сюда, например, .glb файл." : $"Файлов: {files.Length}. Выберите файл, чтобы скопировать запрос для агента.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void CopySelection(bool request)
    {
        var paths = _files.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag!).ToArray();
        if (paths.Length == 0) { _status.Text = "Выберите файл из списка."; return; }
        var text = string.Join(Environment.NewLine, paths);
        if (request)
        {
            var project = Adapters.Godot.GodotSettingsStore.Load().ProjectRoot;
            text = $"Установи этот ассет в активный Godot-проект {project}:\n{text}";
        }
        try { Clipboard.SetText(text); _status.Text = request ? "Запрос скопирован. Вставьте его в чат с агентом." : "Путь скопирован."; }
        catch (Exception ex) { _status.Text = "Буфер обмена занят: " + ex.Message; }
    }

    protected override void Dispose(bool disposing)
    { if (disposing) _refresh.Dispose(); base.Dispose(disposing); }
}
