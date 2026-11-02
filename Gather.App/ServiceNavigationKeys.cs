using System.Runtime.InteropServices;
namespace Gather.App;

// WebView2 sends keyboard input to its own child HWND. A thread-local message
// hook handles Gather's Ctrl+Tab and Ctrl+K before either XAML or Chromium.
// It neither observes other applications nor reads text input.
internal sealed class ServiceNavigationKeys : IDisposable
{
    private readonly HookProc _handler;
    private readonly nint _hook;
    public ServiceNavigationKeys(nint window, Func<int, bool> navigate, Func<bool> quickSwitch)
    {
        _handler = (code, removed, message) =>
        {
            if (code >= 0 && removed == 1
                && (uint)Marshal.ReadInt32(message, IntPtr.Size) == 0x100
                && (GetKeyState(0x11) & 0x8000) != 0
                && (GetKeyState(0x12) & 0x8000) == 0
                && GetAncestor(Marshal.ReadIntPtr(message), 2) == window)
            {
                var key = Marshal.ReadIntPtr(message, IntPtr.Size == 8 ? 16 : 8);
                bool shift = (GetKeyState(0x10) & 0x8000) != 0;
                bool handled = key == 9 ? navigate(shift ? -1 : 1)
                    : key == 0x4B && !shift && quickSwitch();
                if (handled)
                    Marshal.WriteInt32(message, IntPtr.Size, 0); // WM_NULL prevents double navigation.
            }
            return CallNextHookEx(0, code, removed, message);
        };
        _hook = SetWindowsHookEx(3, _handler, 0, GetCurrentThreadId());
    }
    public bool Available => _hook != 0;
    public void Dispose()
    {
        if (_hook != 0)
            UnhookWindowsHookEx(_hook);
    }
    private delegate nint HookProc(int code, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
