using System.Runtime.InteropServices;

namespace ConstantineHub.Core;

internal static class TrayIdentity
{
    // Keep these stable across versions and the Constantine Hub -> ConHub. display rebrand.
    internal const string AppUserModelId = "Constantine.ConHub";
    internal static readonly Guid IconGuid = new("5594a1da-dff9-4ab0-9a21-b45a93a378e1");

    internal static void Initialize()
        => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppUserModelId));

    internal static bool ShouldEnableTray(IReadOnlyDictionary<string, string> options)
        => !options.ContainsKey("no-tray") && !options.ContainsKey("isolated-instance");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}

/// <summary>One GUID-based shell registration; construction alone has no Windows side effects.</summary>
internal sealed class StableTrayIcon : IDisposable
{
    private readonly Func<uint, NotifyIconData, bool> _send;
    private readonly bool _native;
    private TrayWindow? _window;
    private bool _visible;
    private bool _disposed;
    private string _text = "ConHub.";
    private Icon? _icon;
    internal bool IsRegistered { get; private set; }
    internal string? LastError { get; private set; }
    internal ContextMenuStrip? ContextMenuStrip { get; set; }
    internal event EventHandler? DoubleClick;

    internal StableTrayIcon() { _native = true; _send = NativeSend; }
    internal StableTrayIcon(Func<uint, NotifyIconData, bool> send) => _send = send;

    internal string Text
    {
        get => _text;
        set { _text = value; Modify(); }
    }
    internal Icon? Icon
    {
        get => _icon;
        set { _icon = value; Modify(); }
    }
    internal bool Visible
    {
        get => _visible;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_visible == value) return;
            _visible = value;
            if (value) Add(); else Delete();
        }
    }

    private NotifyIconData Data(uint flags = 0x1 | 0x2 | 0x4 | 0x20) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window?.Handle ?? IntPtr.Zero,
        Id = 1,
        Flags = flags,
        CallbackMessage = TrayWindow.Callback,
        Icon = _icon?.Handle ?? IntPtr.Zero,
        Tip = _text.Length > 127 ? _text[..127] : _text,
        Guid = TrayIdentity.IconGuid,
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private void Add()
    {
        if (IsRegistered || _disposed) return;
        if (_native) _window ??= new TrayWindow(this);
        IsRegistered = _send(0, Data()); // NIM_ADD; never generate a replacement GUID on failure.
        LastError = IsRegistered ? null : "Windows could not register the ConHub. tray icon. Keep the executable at its installed path; unsigned GUID icons are bound to that path.";
    }

    private void Modify()
    {
        if (IsRegistered && !_send(1, Data())) LastError = "Windows could not update the ConHub. tray icon.";
    }

    private void Delete()
    {
        if (!IsRegistered) return;
        _send(2, Data(0x20)); // NIM_DELETE for precisely our GUID.
        IsRegistered = false;
    }

    internal void ExplorerRestarted()
    {
        IsRegistered = false;
        if (_visible && !_disposed) Add();
    }

    internal void ShowBalloonTip(int timeout, string title, string text, ToolTipIcon icon)
    {
        if (!IsRegistered) return;
        var data = Data(0x20 | 0x10);
        data.InfoTitle = title.Length > 63 ? title[..63] : title;
        data.Info = text.Length > 255 ? text[..255] : text;
        data.TimeoutOrVersion = (uint)Math.Max(0, timeout);
        data.InfoFlags = (uint)icon;
        _send(1, data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Delete();
        _visible = false;
        _disposed = true;
        _window?.DestroyHandle();
        _window = null;
    }

    private static bool NativeSend(uint operation, NotifyIconData data)
        => Shell_NotifyIcon(operation, ref data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        internal uint Size;
        internal IntPtr Window;
        internal uint Id, Flags, CallbackMessage;
        internal IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid Guid;
        internal IntPtr BalloonIcon;
    }

    private sealed class TrayWindow : NativeWindow
    {
        internal const int Callback = 0x8000 + 21;
        private readonly StableTrayIcon _owner;
        private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        internal TrayWindow(StableTrayIcon owner)
        {
            _owner = owner;
            // A hidden top-level window receives Explorer's TaskbarCreated broadcast.
            CreateHandle(new CreateParams { Caption = "ConHub. tray messages" });
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == _taskbarCreated) _owner.ExplorerRestarted();
            if (message.Msg == Callback)
            {
                if (message.LParam.ToInt32() == 0x0203) _owner.DoubleClick?.Invoke(_owner, EventArgs.Empty);
                if (message.LParam.ToInt32() is 0x0205 or 0x007B)
                {
                    SetForegroundWindow(Handle);
                    _owner.ContextMenuStrip?.Show(Cursor.Position);
                    PostMessage(Handle, 0, IntPtr.Zero, IntPtr.Zero);
                }
            }
            base.WndProc(ref message);
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string text);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
