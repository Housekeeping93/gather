using System.Diagnostics;
using System.Text;

namespace Gather.Signal;

public sealed class SignalRuntime : IDisposable
{
    public const string TestedProtocolVersion = "0.14.7";
    private readonly Process _process;
    private readonly FileStream _accountLock;
    private readonly OwnedProcessJob _job;
    public SignalRpcConnection Connection { get; }
    internal int ProcessId => _process.Id;
    public static bool IsAvailable(string baseDirectory) => RuntimeManifest.IsValid(Path.Combine(baseDirectory, "runtimes", "signal"));

    public static ProcessStartInfo CreateStartInfo(string baseDirectory, string dataDirectory)
    {
        string runtime = Path.GetFullPath(Path.Combine(baseDirectory, "runtimes", "signal"));
        var info = new ProcessStartInfo(Path.Combine(runtime, "java", "bin", "java.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = runtime
        };
        foreach (var argument in new[] { "-Dfile.encoding=UTF-8", "-cp", Path.Combine(runtime, "signal-cli", "lib", "*"),
            "org.asamk.signal.Main", "--data-dir", Path.GetFullPath(dataDirectory), "--scrub-log", "--disable-send-log",
            "--trust-new-identities", "on-first-use", "jsonRpc", "--receive-mode", "manual",
            "--ignore-attachments", "--ignore-stickers", "--ignore-stories" }) info.ArgumentList.Add(argument);
        return info;
    }
    public static async Task<SignalRuntime> StartAsync(string baseDirectory, string dataDirectory, CancellationToken cancellationToken)
    {
        // Hash the distribution off the UI thread, then honor cancellation before
        // creating account files or starting a child process.
        bool available = await Task.Run(() => IsAvailable(baseDirectory), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!available) throw new FileNotFoundException("The Signal runtime is missing, incomplete or has changed.");
        return new SignalRuntime(baseDirectory, dataDirectory);
    }
    private SignalRuntime(string baseDirectory, string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _accountLock = new FileStream(Path.Combine(dataDirectory, "gather.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _process = new Process { StartInfo = CreateStartInfo(baseDirectory, dataDirectory) };
        try
        {
            if (!_process.Start()) throw new IOException("Signal process did not start.");
            _job = new(_process);
            Connection = new(_process.StandardOutput, _process.StandardInput);
            _ = DrainErrors();
        }
        catch
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            _process.Dispose(); _accountLock.Dispose(); throw;
        }
    }
    private async Task DrainErrors()
    {
        // Drain without retaining or logging potentially sensitive upstream diagnostics.
        var buffer = new char[2048];
        try { while (await _process.StandardError.ReadAsync(buffer) != 0) Array.Clear(buffer); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
        finally { Array.Clear(buffer); }
    }
    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Connection.Dispose();
        try { _job.Dispose(); if (!_process.HasExited) { _process.Kill(entireProcessTree: true); _process.WaitForExit(2000); } }
        catch (InvalidOperationException) { }
        finally { _process.Dispose(); _accountLock.Dispose(); }
    }
}
