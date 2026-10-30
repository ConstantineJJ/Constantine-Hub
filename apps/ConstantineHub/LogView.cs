namespace ConstantineHub;

internal sealed partial class MainForm
{
    internal const int LogViewCharacterLimit = 250000;
    private bool _logDirty = true;
    private string? _renderedLogText;

    internal void FlushLogView()
    {
        // Collect diagnostics in memory while hidden/collapsed. Native RichEdit
        // updates are only needed when the user can see the log.
        if (!_logDirty || IsDisposed || Disposing || !Visible || !_log.Visible || !_log.IsHandleCreated) return;
        var text = string.Join(Environment.NewLine,
            _allLogLines.Where(line => _verboseLogs || !IsVerboseTunnelLine(line)));
        if (text.Length > LogViewCharacterLimit) text = text[^LogViewCharacterLimit..];
        _logDirty = false;
        if (text == _renderedLogText) return;

        // Replacing SelectedText with "" is rejected by a read-only RichEdit.
        // Replace its bounded plain-text snapshot instead; no editing command
        // is sent to the protected selection, and history cannot grow forever.
        _log.Text = text;
        _renderedLogText = text;
        _log.ClearUndo();
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}
