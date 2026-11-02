using System.Runtime.InteropServices;
namespace Gather.App;

internal sealed class TaskbarBadge(nint hwnd) : IDisposable
{
    private ITaskbarList3? _taskbar;
    private string _label = "", _description = "", _applied = "";
    public int? LastResult
    {
        get; private set;
    }
    public void Set(string label, string description)
    {
        _label = label;
        _description = description;
        Apply();
    }
    public void Refresh()
    {
        _applied = "?";
        Apply();
    }
    private void Apply()
    {
        if (_applied == _label)
            return;
        // Cache unsuccessful attempts too. Retry on a changed badge or when
        // Explorer creates/restores the taskbar button, not on every status event.
        _applied = _label;
        nint icon = 0;
        try
        {
            _taskbar ??= (ITaskbarList3)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"))!)!;
            LastResult = _taskbar.HrInit();
            if (LastResult < 0)
                return;
            if (_label.Length > 0)
            {
                var bytes = IconBytes(_label);
                icon = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x00030000, 32, 32, 0);
                if (icon == 0)
                    return;
            }
            LastResult = _taskbar.SetOverlayIcon(hwnd, icon, _description);
            if (LastResult >= 0)
                _applied = _label;
        }
        catch (COMException) { }
        finally { if (icon != 0) DestroyIcon(icon); }
    }
    // A small alpha icon, generated only when its label changes. No UI timer,
    // image files, drawing runtime or per-message native handles are retained.
    internal static byte[] IconBytes(string label)
    {
        const int size = 32;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double coverage = 0, border = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        double dx = x + (sx + .5) / 4 - 16, dy = y + (sy + .5) / 4 - 16;
                        double d = dx * dx + dy * dy;
                        if (d < 15.5 * 15.5)
                        {
                            coverage++;
                            if (d > 13.5 * 13.5)
                                border++;
                        }
                    }
                if (coverage == 0)
                    continue;
                int i = ((size - 1 - y) * size + x) * 4;
                double edge = border / coverage;
                pixels[i] = (byte)(62 + 193 * edge);
                pixels[i + 1] = (byte)(48 + 207 * edge);
                pixels[i + 2] = (byte)(193 + 62 * edge);
                pixels[i + 3] = (byte)(255 * coverage / 16);
            }
        if (label != "•")
        {
            string[] glyphs = ["111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001", "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111", "000010111010000"];
            int scale = label.Length == 1 ? 4 : 2;
            int width = (label.Length * 4 - 1) * scale;
            int left = (size - width) / 2, top = (size - 5 * scale) / 2;
            for (int c = 0; c < label.Length; c++)
            {
                int glyph = label[c] == '+' ? 10 : label[c] - '0';
                if (glyph is < 0 or > 10)
                    continue;
                for (int y = 0; y < 5 * scale; y++)
                    for (int x = 0; x < 3 * scale; x++)
                        if (glyphs[glyph][y / scale * 3 + x / scale] == '1')
                        {
                            int i = ((size - 1 - (top + y)) * size + left + c * 4 * scale + x) * 4;
                            pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 255;
                        }
            }
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(pixels.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(pixels);
        writer.Write(new byte[size * 4]);
        return stream.ToArray();
    }
    public void Dispose()
    {
        if (_taskbar == null)
            return;
        try
        {
            _taskbar.SetOverlayIcon(hwnd, 0, "");
        }
        catch (COMException) { }
        Marshal.ReleaseComObject(_taskbar);
        _taskbar = null;
    }
    [DllImport("user32.dll")] private static extern nint CreateIconFromResourceEx(byte[] data, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);

    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(nint hwnd);
        [PreserveSig] int DeleteTab(nint hwnd);
        [PreserveSig] int ActivateTab(nint hwnd);
        [PreserveSig] int SetActiveAlt(nint hwnd);
        [PreserveSig] int MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        [PreserveSig] int SetProgressValue(nint hwnd, ulong completed, ulong total);
        [PreserveSig] int SetProgressState(nint hwnd, uint flags);
        [PreserveSig] int RegisterTab(nint tab, nint mdi);
        [PreserveSig] int UnregisterTab(nint tab);
        [PreserveSig] int SetTabOrder(nint tab, nint before);
        [PreserveSig] int SetTabActive(nint tab, nint mdi, uint flags);
        [PreserveSig] int ThumbBarAddButtons(nint hwnd, uint count, nint buttons);
        [PreserveSig] int ThumbBarUpdateButtons(nint hwnd, uint count, nint buttons);
        [PreserveSig] int ThumbBarSetImageList(nint hwnd, nint imageList);
        [PreserveSig] int SetOverlayIcon(nint hwnd, nint icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
    }
}
