using System.Collections.Concurrent;
using System.Text.Json;

namespace Gather.Signal;

// The transport owns neither a shell nor a network listener. Parameters are JSON data.
public sealed class SignalRpcConnection : IDisposable
{
    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writes = new(1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private long _nextId;
    private int _started, _closed;
    public event Action<JsonElement>? Notification;
    public event Action? Disconnected;
    public SignalRpcConnection(TextReader reader, TextWriter writer) => (_reader, _writer) = (reader, writer);
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Connection already started.");
        _ = ReadLoop();
    }
    public async Task<JsonElement> CallAsync(string method, object? parameters = null,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null)
    {
        if (_closed != 0) throw new IOException("Signal connection is closed.");
        string id = Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        request.CancelAfter(timeout ?? TimeSpan.FromSeconds(45));
        try
        {
            string json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } });
            await _writes.WaitAsync(request.Token);
            try
            {
                await _writer.WriteLineAsync(json.AsMemory(), request.Token);
                await _writer.FlushAsync(request.Token);
            }
            finally { _writes.Release(); }
            return await completion.Task.WaitAsync(request.Token);
        }
        finally { _pending.TryRemove(id, out _); }
    }
    private async Task ReadLoop()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                string? line = await _reader.ReadLineAsync(_lifetime.Token);
                if (line is null) break;
                if (line.Length == 0) continue;
                if (line.Length > 4 * 1024 * 1024) throw new IOException("Signal response exceeds the limit.");
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    if (!_pending.TryRemove(id.ToString(), out var pending)) continue; // Expired or cancelled request.
                    if (root.TryGetProperty("error", out var error))
                        pending.TrySetException(new SignalRpcException(error.TryGetProperty("code", out var code) && code.TryGetInt32(out int n) ? n : 0));
                    else if (root.TryGetProperty("result", out var result)) pending.TrySetResult(result.Clone());
                    else pending.TrySetException(new IOException("Incomplete Signal response."));
                }
                else if (root.TryGetProperty("method", out _)) Notification?.Invoke(root.Clone());
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
        finally { Stop(); }
    }
    private void Stop()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _lifetime.Cancel();
        foreach (var pair in _pending)
            if (_pending.TryRemove(pair.Key, out var pending)) pending.TrySetException(new IOException("Signal connection was interrupted."));
        Disconnected?.Invoke();
    }
    public void Dispose() => Stop();
}

// Upstream error text can contain recipients, message text or provisioning secrets.
public sealed class SignalRpcException(int code) : Exception("Signal could not complete this request.")
{
    public int Code { get; } = code;
}
