using Gather.Core;
using Gather.Signal;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

internal static class SignalChecks
{
    private static void Expect(bool condition) { if (!condition) throw new Exception("Signal assertion failed."); }
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
    internal static void Run(Action<string, Action> check, string[] args)
    {
        RuntimeChecks.Run(check, args);
        check("Signal is a native sixth provider and cannot enter browser navigation", () =>
        {
            var signal = Providers.Find("signal")!;
            Expect(Providers.All.Length == 6 && signal.Kind == ProviderKind.Signal);
            Expect(NavigationPolicy.Decide(signal, "https://signal.org/") == NavigationDecision.Block);
            Expect(NavigationPolicy.Decide(signal, "sgnl://linkdevice?uuid=secret") == NavigationDecision.Block);
        });
        check("Mixed web/Signal layouts retain both identities across navigation and restart", () =>
        {
            var a = new Account { ProviderId = "whatsapp" }; var b = new Account { ProviderId = "signal" };
            var state = new Settings { Accounts = [a, b] };
            LayoutActions.EnableSplit(state.Layout, [a.Id, b.Id]); LayoutActions.Swap(state.Layout);
            LayoutActions.Normalize(state); Expect(state.Layout.Left == b.Id && state.Accounts.Count == 2);
            LayoutActions.Navigate(state.Layout, [a.Id, b.Id], 1); Expect(state.Layout.Right == b.Id);
        });
        check("Preview settings migrate without overwriting settings used by the older release", () =>
        {
            string dir = Temp(); var original = new Settings { Accounts = [new() { ProviderId = "slack" }] };
            new SettingsStore(dir).Save(original); byte[] before = File.ReadAllBytes(Path.Combine(dir, "settings.json"));
            var preview = new SettingsStore(dir, "settings-signal-preview.json"); var state = preview.Load();
            state.Accounts.Add(new() { ProviderId = "signal" }); preview.Save(state);
            Expect(File.ReadAllBytes(Path.Combine(dir, "settings.json")).SequenceEqual(before));
            Expect(preview.Load().Accounts.Count == 2 && new SettingsStore(dir).Load().Accounts.Count == 1);
        });
        check("Signal process uses explicit arguments, isolated data and restricted receive options", () =>
        {
            var info = SignalRuntime.CreateStartInfo("C:\\gather with spaces", "C:\\profiles\\one & two");
            Expect(!info.UseShellExecute && info.CreateNoWindow && info.RedirectStandardInput);
            Expect(info.ArgumentList.Contains("C:\\profiles\\one & two"));
            Expect(info.ArgumentList.Contains("--ignore-attachments") && info.ArgumentList.Contains("--disable-send-log"));
            Expect(!info.ArgumentList.Contains("--ignore-avatars"));
            Expect(!info.ArgumentList.Contains("always") && !info.ArgumentList.Contains("register"));
        });
        check("Windows terminates an owned child process when its Gather job closes", () =>
        {
            using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
                { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--signal-process-fixture" } })!;
            try
            {
                using (var job = new OwnedProcessJob(child)) { Expect(!child.HasExited); }
                Expect(child.WaitForExit(3000));
            }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        });
        check("Signal RPC matches out-of-order responses and receives independent notifications", () => Async(async () =>
        {
            using var peer = new Peer(); int notices = 0; peer.Rpc.Notification += _ => notices++;
            var first = peer.Rpc.CallAsync("first", new { text = "Quotes \" and\nnewlines" });
            var second = peer.Rpc.CallAsync("second");
            var request1 = await peer.Request(); var request2 = await peer.Request();
            Expect(request1.GetProperty("params").GetProperty("text").GetString()!.Contains('\n'));
            peer.Reply(request2, "2"); peer.Input.Lines.Writer.TryWrite("{\"jsonrpc\":\"2.0\",\"method\":\"receive\",\"params\":{}}"); peer.Reply(request1, "1");
            Expect((await first).GetInt32() == 1 && (await second).GetInt32() == 2 && notices == 1);
        }));
        check("Signal RPC cancellation ignores late replies without breaking later requests", () => Async(async () =>
        {
            using var peer = new Peer(); using var cancellation = new CancellationTokenSource();
            var abandoned = peer.Rpc.CallAsync("startLink", cancellationToken: cancellation.Token); var request = await peer.Request();
            cancellation.Cancel(); try { await abandoned; throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
            peer.Reply(request, "false"); var next = peer.Rpc.CallAsync("listAccounts"); peer.Reply(await peer.Request(), "[]");
            Expect((await next).GetArrayLength() == 0);
        }));
        check("Signal RPC EOF releases pending requests and errors do not expose upstream data", () => Async(async () =>
        {
            using var peer = new Peer(); var call = peer.Rpc.CallAsync("send"); var request = await peer.Request();
            peer.Input.Lines.Writer.TryWrite(JsonSerializer.Serialize(new { id = request.GetProperty("id").GetString(), error = new { code = -5, message = "private message secret", data = "sgnl://secret" } }));
            try { await call; throw new Exception("Error ignored."); } catch (SignalRpcException e) { Expect(e.Code == -5 && !e.ToString().Contains("secret")); }
            var pending = peer.Rpc.CallAsync("next"); await peer.Request(); peer.Input.Lines.Writer.Complete();
            try { await pending; throw new Exception("EOF ignored."); } catch (Exception e) when (e is IOException or OperationCanceledException) { }
        }));
        check("Signal account linking uses existing-device methods and restores the linked number", () => Async(async () =>
        {
            using var peer = new Peer(); var client = new SignalAccountClient(peer.Rpc);
            var start = client.StartLinkAsync(default); var request = await peer.Request(); Expect(request.GetProperty("method").GetString() == "startLink");
            peer.Reply(request, "{\"deviceLinkUri\":\"sgnl://linkdevice?uuid=test&pub_key=test\"}"); string uri = await start;
            var finish = client.FinishLinkAsync(uri, default); request = await peer.Request(); Expect(request.GetProperty("method").GetString() == "finishLink");
            peer.Reply(request, "{}"); request = await peer.Request(); peer.Reply(request, "[{\"number\":\"+32000000000\"}]");
            await finish; Expect(client.Number == "+32000000000");
        }));
        check("Signal profile pictures use the pinned avatar command and linked account", () => Async(async () =>
        {
            using var peer = new Peer(); var client = new SignalAccountClient(peer.Rpc);
            var restore = client.RestoreAsync(default); peer.Reply(await peer.Request(), "[{\"number\":\"+32000000000\"}]"); await restore;
            var avatar = client.AvatarAsync("+32123456789", false, default); var request = await peer.Request();
            Expect(request.GetProperty("method").GetString() == "getAvatar");
            Expect(request.GetProperty("params").GetProperty("account").GetString() == "+32000000000");
            Expect(request.GetProperty("params").GetProperty("profile").GetString() == "+32123456789");
            peer.Reply(request, "{\"data\":\"AQID\"}"); Expect((await avatar)!.SequenceEqual(new byte[] { 1, 2, 3 }));
        }));
        check("A missing profile picture refreshes the profile and falls back to its contact picture", () => Async(async () =>
        {
            using var peer = new Peer(); var client = new SignalAccountClient(peer.Rpc);
            var restore = client.RestoreAsync(default); peer.Reply(await peer.Request(), "[{\"number\":\"+32000000000\"}]"); await restore;
            var avatar = client.AvatarAsync("+32123456789", false, default);
            var request = await peer.Request();
            peer.Input.Lines.Writer.TryWrite(JsonSerializer.Serialize(new { id = request.GetProperty("id").GetString(), error = new { code = -1 } }));
            request = await peer.Request(); Expect(request.GetProperty("method").GetString() == "listContacts");
            Expect(request.GetProperty("params").GetProperty("recipient")[0].GetString() == "+32123456789"); peer.Reply(request, "[]");
            request = await peer.Request(); Expect(request.GetProperty("params").TryGetProperty("profile", out _)); peer.Reply(request, "{\"data\":null}");
            request = await peer.Request(); Expect(request.GetProperty("params").GetProperty("contact").GetString() == "+32123456789");
            peer.Reply(request, "{\"data\":\"BAUG\"}"); Expect((await avatar)!.SequenceEqual(new byte[] { 4, 5, 6 }));
        }));
        check("Group pictures use groupId and absent pictures remain optional", () => Async(async () =>
        {
            using var peer = new Peer(); var client = new SignalAccountClient(peer.Rpc);
            var restore = client.RestoreAsync(default); peer.Reply(await peer.Request(), "[{\"number\":\"+32000000000\"}]"); await restore;
            var avatar = client.AvatarAsync("group-id", true, default); var request = await peer.Request();
            Expect(request.GetProperty("params").GetProperty("groupId").GetString() == "group-id");
            peer.Input.Lines.Writer.TryWrite(JsonSerializer.Serialize(new { id = request.GetProperty("id").GetString(), error = new { code = -1 } }));
            Expect(await avatar is null);
            var contacts = client.ContactsAsync(default); peer.Reply(await peer.Request(), "[]"); Expect((await contacts).GetArrayLength() == 0);
        }));
        check("Malformed or oversized avatar payloads are rejected", () =>
        {
            Expect(SignalAvatar.Decode(Json("{}")) is null);
            Expect(SignalAvatar.Decode(Json("{\"data\":\"https://untrusted.example/picture.png\"}")) is null);
            Expect(SignalAvatar.Decode(Json("{\"data\":\"%%%\"}")) is null);
            Expect(SignalAvatar.Decode(JsonSerializer.SerializeToElement(new { data = Convert.ToBase64String(new byte[SignalAvatar.MaximumBytes + 1]) })) is null);
        });
        check("Signal receive deduplicates, filters other accounts and preserves real unread state", () =>
        {
            var history = new SignalHistory(); var message = Envelope("hello", 100);
            Expect(history.Apply(message, "+32000000000") != null); Expect(history.Apply(message, "+32000000000") == null);
            Expect(history.Conversations.Single().UnreadCount == 1);
            var other = Json("{\"method\":\"receive\",\"params\":{\"account\":\"+999999\",\"envelope\":{\"source\":\"+32123456789\",\"dataMessage\":{\"timestamp\":200,\"message\":\"wrong account\"}}}}");
            history.Apply(other, "+32000000000"); Expect(history.Conversations.Single().Messages.Count == 1);
        });
        check("Disappearing and view-once messages never enter ordinary history", () =>
        {
            var history = new SignalHistory(); history.Apply(Envelope("secret", 100, "\"expiresInSeconds\":60"), "+32000000000");
            history.Apply(Envelope("secret", 200, "\"viewOnce\":true"), "+32000000000");
            Expect(history.Conversations.All(c => c.Messages.Count == 0)); Expect(!JsonSerializer.Serialize(history).Contains("secret"));
        });
        check("Remote deletion removes its message and prevents replay from restoring it", () =>
        {
            var history = new SignalHistory(); history.Apply(Envelope("remove me", 100), "+32000000000");
            history.Apply(Envelope(null, 200, "\"remoteDelete\":{\"timestamp\":100}"), "+32000000000");
            history.Apply(Envelope("remove me", 100), "+32000000000");
            Expect(history.Conversations.Single().Messages.Count == 0 && !JsonSerializer.Serialize(history).Contains("remove me"));
        });
        check("Sent-device sync is not unread and uncertain sends are never automatically retried", () =>
        {
            var history = new SignalHistory();
            history.Apply(Json("{\"method\":\"receive\",\"params\":{\"envelope\":{\"sourceNumber\":\"+32000000000\",\"syncMessage\":{\"sentMessage\":{\"destination\":\"+32123456789\",\"timestamp\":100,\"message\":\"sent on phone\"}}}}}"), "+32000000000");
            var chat = history.Conversations.Single(); Expect(chat.Messages.Single().Outgoing && chat.UnreadCount == 0);
            chat.Messages.Add(new() { Outgoing = true, Status = "Sending" }); history.RecoverInterruptedSends();
            Expect(chat.Messages.Last().Status.StartsWith("Delivery unknown"));
        });
        check("Local Signal history is user-protected and survives a round trip", () =>
        {
            string directory = Temp(); var store = new ProtectedHistoryStore(directory); var history = new SignalHistory();
            history.Get("+32123456789", false).Messages.Add(new() { Text = "private-content-marker" }); store.Save(history);
            Expect(store.Load().Conversations.Single().Messages.Single().Text == "private-content-marker");
            Expect(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "history.dat"))).Contains("private-content-marker"));
        });
        check("Pinned contact schema honors blocked contacts and disappearing-message settings", () =>
        {
            var history = new SignalHistory();
            history.UpdateContacts(Json("[{\"number\":\"+32123456789\",\"name\":\"Contact\",\"isBlocked\":true,\"messageExpirationTime\":60}]"));
            history.Apply(Envelope("blocked content", 100), "+32000000000");
            Expect(history.Conversations.Single().Blocked && history.Conversations.Single().ExpirationSeconds == 60);
            Expect(!JsonSerializer.Serialize(history).Contains("blocked content"));
        });
        check("Tampered protected history fails without overwriting the original file", () =>
        {
            string directory = Temp(); var store = new ProtectedHistoryStore(directory);
            string file = Path.Combine(directory, "history.dat"); File.WriteAllBytes(file, [1, 2, 3, 4]);
            try { store.Load(); throw new Exception("Invalid history was accepted."); } catch (System.ComponentModel.Win32Exception) { }
            Expect(File.ReadAllBytes(file).SequenceEqual(new byte[] { 1, 2, 3, 4 }));
        });
        check("QR encoder handles bounded UTF-8 input and produces full version-10 modules", () =>
        {
            Expect(LinkQrCode.Encode("sgnl://linkdevice?uuid=test&pub_key=test").GetLength(0) == 57);
            Expect(LinkQrCode.Encode(new string('x', 271)).GetLength(1) == 57);
            try { LinkQrCode.Encode(new string('x', 272)); throw new Exception("QR input limit ignored."); } catch (ArgumentException) { }
        });
        if (args.Length == 2 && args[0] == "--qr-fixtures")
        {
            Directory.CreateDirectory(args[1]);
            var fixtures = new[] { "sgnl://linkdevice?uuid=test&pub_key=test", "sgnl://linkdevice?uuid=12345678-1234-1234-1234-123456789abc&pub_key=BQ%2FabC123%2F%2B%3D", new string('x', 271), "UTF-8: é 日本語 🔒" };
            for (int i = 0; i < fixtures.Length; i++)
            {
                var matrix = LinkQrCode.Encode(fixtures[i]); var rows = new string[57];
                for (int y = 0; y < 57; y++) rows[y] = new string(Enumerable.Range(0, 57).Select(x => matrix[x, y] ? '1' : '0').ToArray());
                File.WriteAllText(Path.Combine(args[1], $"qr-{i}.json"), JsonSerializer.Serialize(new { text = fixtures[i], rows }));
            }
        }
    }
    private static string Temp() { string dir = Path.Combine(Path.GetTempPath(), "Gather-Signal-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); return dir; }
    private static void Async(Func<Task> action) => action().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    private static JsonElement Envelope(string? message, long timestamp, string extra = "\"expiresInSeconds\":0") =>
        Json("{\"method\":\"receive\",\"params\":{\"envelope\":{\"sourceNumber\":\"+32123456789\",\"sourceName\":\"Contact\",\"dataMessage\":{\"timestamp\":" + timestamp + ",\"message\":" + JsonSerializer.Serialize(message) + "," + extra + "}}}}");
    private sealed class Peer : IDisposable
    {
        public LinesReader Input { get; } = new(); public LinesWriter Output { get; } = new(); public SignalRpcConnection Rpc { get; }
        public Peer() { Rpc = new(Input, Output); Rpc.Start(); }
        public async Task<JsonElement> Request() => Json(await Output.Lines.Reader.ReadAsync());
        public void Reply(JsonElement request, string result) => Input.Lines.Writer.TryWrite("{\"jsonrpc\":\"2.0\",\"id\":" + request.GetProperty("id").GetRawText() + ",\"result\":" + result + "}");
        public void Dispose() { Rpc.Dispose(); Input.Lines.Writer.TryComplete(); }
    }
    private sealed class LinesReader : TextReader
    {
        public Channel<string> Lines { get; } = Channel.CreateUnbounded<string>();
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        { try { return await Lines.Reader.ReadAsync(cancellationToken); } catch (ChannelClosedException) { return null; } }
    }
    private sealed class LinesWriter : TextWriter
    {
        public Channel<string> Lines { get; } = Channel.CreateUnbounded<string>(); public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) => Lines.Writer.WriteAsync(buffer.ToString(), cancellationToken).AsTask();
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
