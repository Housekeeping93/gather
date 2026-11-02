using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Gather.Signal;

public sealed class ProtectedHistoryStore(string directory)
{
    public SignalHistory Load()
    {
        string path = Path.Combine(directory, "history.dat");
        if (!File.Exists(path)) return new();
        byte[] clear = Protect(File.ReadAllBytes(path), decrypt: true);
        try
        {
            var history = JsonSerializer.Deserialize<SignalHistory>(clear) ?? throw new JsonException("Invalid history.");
            history.RecoverInterruptedSends();
            return history;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void Save(SignalHistory history)
    {
        Directory.CreateDirectory(directory);
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(history);
        try
        {
            byte[] sealedData = Protect(clear, decrypt: false);
            string path = Path.Combine(directory, "history.dat"), temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(sealedData); stream.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private static byte[] Protect(byte[] data, bool decrypt)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Signal history requires Windows user-bound protection.");
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        var input = new Blob { Size = data.Length, Data = pin.AddrOfPinnedObject() };
        Blob output = default;
        try
        {
            bool ok = decrypt ? CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output) : CryptProtectData(ref input, "Gather Signal history", 0, 0, 0, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "Signal history could not be protected or opened by this Windows user.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally { pin.Free(); if (output.Data != 0) LocalFree(output.Data); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
