using System.Runtime.InteropServices;
using Gather.Core;
namespace Gather.App;

// WebView2 sends keyboard input to its own child HWND. A thread-local message
// hook handles Gather's shortcuts before either XAML or Chromium.
// It neither observes other applications nor reads text input.
internal sealed class ServiceNavigationKeys : IDisposable
{
    private readonly HookProc _handler;
    private readonly nint _hook;
    public ServiceNavigationKeys(Func<nint, bool> ownsWindow, Func<int, bool> navigate, Func<bool> quickSwitch,
        Func<bool> focusView, Func<bool> splitView, Func<bool> privacy, Func<string> privacyShortcut)
    {
        _handler = (code, removed, message) =>
        {
            if (code >= 0 && removed == 1
                && (uint)Marshal.ReadInt32(message, IntPtr.Size) is 0x100 or 0x104
                && ownsWindow(GetAncestor(Marshal.ReadIntPtr(message), 2)))
            {
                var key = Marshal.ReadIntPtr(message, IntPtr.Size == 8 ? 16 : 8);
                bool shift = (GetKeyState(0x10) & 0x8000) != 0;
                var shortcut = ShellShortcuts.Resolve((int)key, (GetKeyState(0x11) & 0x8000) != 0,
                    shift, (GetKeyState(0x12) & 0x8000) != 0, privacyShortcut());
                bool repeat = ((long)Marshal.ReadIntPtr(message, IntPtr.Size == 8 ? 24 : 12) & (1L << 30)) != 0;
                bool handled = shortcut switch
                {
                    ShellShortcut.Previous => navigate(-1),
                    ShellShortcut.Next => navigate(1),
                    ShellShortcut.QuickSwitch => quickSwitch(),
                    ShellShortcut.FocusView => repeat || focusView(),
                    ShellShortcut.SplitView => repeat || splitView(),
                    ShellShortcut.PrivacyCurtain => repeat || privacy(),
                    _ => false
                };
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
