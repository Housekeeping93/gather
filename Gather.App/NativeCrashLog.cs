using System.Runtime.InteropServices;
using System.Text;

namespace Gather.App;

// WinUI can terminate with a stowed native exception before the managed
// UnhandledException event. Record error codes and module offsets only;
// never write process dumps, exception text, web content or credentials.
internal static class NativeCrashLog
{
    private static readonly Handler Callback = Observe;
    private static nint _registration;
    [ThreadStatic] private static bool _observing;

    internal static void Install() => _registration = AddVectoredExceptionHandler(1, Callback);

    private static int Observe(nint pointers)
    {
        if (_observing || IntPtr.Size != 8)
            return 0;
        _observing = true;
        try
        {
            var record = Read(Pointer(Read(pointers, 8), 0), 48);
            if (record == null || BitConverter.ToUInt32(record, 0) != 0xc000027b)
                return 0;
            StartupLog.Write("native-stowed-exception");
            if (BitConverter.ToUInt32(record, 24) < 2)
                return 0;
            int count = (int)Math.Min(16UL, BitConverter.ToUInt64(record, 40));
            var entries = Read(Pointer(record, 32), count * 8);
            for (int i = 0; entries != null && i < count; i++)
            {
                var entry = Read(Pointer(entries, i * 8), 40);
                if (entry == null || BitConverter.ToUInt32(entry, 0) < 40)
                    continue;
                StartupLog.Write($"native-error code=0x{BitConverter.ToUInt32(entry, 8):X8}");
                if ((BitConverter.ToUInt32(entry, 12) & 3) != 1 || BitConverter.ToUInt32(entry, 24) != 8)
                    continue;
                int frames = (int)Math.Min(32U, BitConverter.ToUInt32(entry, 28));
                var stack = Read(Pointer(entry, 32), frames * 8);
                for (int j = 0; stack != null && j < frames; j++)
                {
                    nint address = Pointer(stack, j * 8);
                    if (!GetModuleHandleEx(6, address, out var module))
                        continue;
                    var path = new StringBuilder(512);
                    GetModuleFileName(module, path, path.Capacity);
                    StartupLog.Write($"native-frame {Path.GetFileName(path.ToString())}+0x{address - module:X}");
                }
            }
        }
        catch { }
        finally { _observing = false; }
        return 0; // Observe only: Windows retains its normal failure handling.
    }

    private static nint Pointer(byte[]? bytes, int offset) => bytes == null ? 0 : (nint)BitConverter.ToInt64(bytes, offset);
    private static byte[]? Read(nint pointer, int size)
    {
        if (pointer == 0 || size <= 0)
            return null;
        var bytes = new byte[size];
        return ReadProcessMemory((nint)(-1), pointer, bytes, (nuint)size, out var read) && read == (nuint)size ? bytes : null;
    }
    private delegate int Handler(nint pointers);
    [DllImport("kernel32.dll")] private static extern nint AddVectoredExceptionHandler(uint first, Handler handler);
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool GetModuleHandleEx(uint flags, nint address, out nint module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(nint module, StringBuilder path, int size);
}
