using System.Runtime.InteropServices;
namespace Gather.App;

internal sealed class NativeWindow : IDisposable
{
    private readonly nint _hwnd;
    private readonly SubclassProc _proc;
    private readonly Action _show, _quit, _openQuiet;
    private readonly Action<bool> _systemChanged;
    private string _quietStatus = "Focus & DND";
    private nint _icon;
    private readonly TaskbarBadge _badge;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarButtonCreated");
    private const uint Callback = 0x8001;
    public bool TrayAvailable
    {
        get;
    }
    public NativeWindow(nint hwnd, Action show, Action quit, Action<bool> systemChanged, Action openQuiet)
    {
        _hwnd = hwnd;
        _badge = new(hwnd);
        _show = show;
        _quit = quit;
        _systemChanged = systemChanged;
        _openQuiet = openQuiet;
        _proc = Handle;
        SetWindowSubclass(hwnd, _proc, 1, 0);
        _icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "Gather.ico"), 1, 32, 32, 0x10);
        if (_icon == 0)
            _icon = LoadIcon(0, (nint)32512);
        SendMessage(hwnd, 0x80, 0, _icon);
        SendMessage(hwnd, 0x80, 1, _icon);
        var data = Data();
        data.uFlags = 1 | 2 | 4;
        data.uCallbackMessage = Callback;
        data.hIcon = _icon;
        data.szTip = "Gather — your messaging space";
        TrayAvailable = Shell_NotifyIcon(0, ref data);
    }
    private NotifyIconData Data() => new() { cbSize = (uint)Marshal.SizeOf<NotifyIconData>(), hWnd = _hwnd, uID = 1, szTip = "", szInfo = "", szInfoTitle = "" };
    private nint Handle(nint hwnd, uint msg, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg is 0x001E or 0x001A) _systemChanged(false); // Clock/timezone/settings change.
        if (msg == 0x0218 && wParam is 4 or 7 or 18) _systemChanged(wParam == 4); // Suspend/resume.
        if (msg == _taskbarCreated)
            _badge.Refresh();
        if (msg == Callback)
        {
            uint evt = (uint)(lParam.ToInt64() & 0xffff);
            // Tray alerts carry no stable per-notification identity. Open the hub
            // instead of guessing which account an older alert belonged to.
            if (evt == 0x202 || evt == 0x203 || evt == 0x405)
                _show();
            if (evt == 0x205)
            {
                var menu = CreatePopupMenu();
                AppendMenu(menu, 1, 0, _quietStatus);
                AppendMenu(menu, 0, 3, "Focus / quiet hours…");
                AppendMenu(menu, 0, 1, "Open Gather");
                AppendMenu(menu, 0, 2, "Quit Gather");
                GetCursorPos(out var point);
                SetForegroundWindow(hwnd);
                var command = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, hwnd, 0);
                DestroyMenu(menu);
                if (command == 1)
                    _show();
                if (command == 2)
                    _quit();
                if (command == 3)
                    _openQuiet();
            }
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }
    public void SetQuietStatus(string status)
    {
        _quietStatus = status;
        if (!TrayAvailable) return;
        var data = Data(); data.uFlags = 4;
        data.szTip = Limit("Gather · " + status, 127);
        Shell_NotifyIcon(1, ref data);
    }
    public void Show()
    {
        ShowWindow(_hwnd, 9);
        SetForegroundWindow(_hwnd);
        _badge.Refresh();
    }
    public void SetBadge(string label, string description) => _badge.Set(label, description);
    public int? BadgeResult => _badge.LastResult;
    public void Hide() => ShowWindow(_hwnd, 0);
    public bool ShowAlert(string title, string body)
    {
        if (!TrayAvailable)
            return false;
        var data = Data();
        data.uFlags = 0x10; // NIF_INFO
        data.szInfoTitle = Limit(title, 63);
        data.szInfo = Limit(body, 255);
        data.dwInfoFlags = 0x1 | 0x80; // NIIF_INFO | NIIF_RESPECT_QUIET_TIME
        return Shell_NotifyIcon(1, ref data);
    }
    private static string Limit(string value, int maximum)
    {
        value = value.Replace('\0', ' ');
        if (value.Length <= maximum)
            return value;
        int length = char.IsHighSurrogate(value[maximum - 2]) ? maximum - 2 : maximum - 1;
        return value[..length] + "…";
    }
    public void Dispose()
    {
        _badge.Dispose();
        var data = Data();
        Shell_NotifyIcon(2, ref data);
        RemoveWindowSubclass(_hwnd, _proc, 1);
    }
    private delegate nint SubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint id, nuint data);
    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize; public nint hWnd; public uint uID, uFlags, uCallbackMessage; public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public nint hBalloonIcon;
    }
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint h, SubclassProc p, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint h, SubclassProc p, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint h, uint m, nuint w, nint l);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint w, nint l);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
}
