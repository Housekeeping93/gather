using System.Text.Json;

namespace Gather.Signal;

public sealed class SignalAccountClient(SignalRpcConnection connection)
{
    public string? Number { get; private set; }
    public async Task<bool> RestoreAsync(CancellationToken token)
    {
        var accounts = await connection.CallAsync("listAccounts", cancellationToken: token);
        if (accounts.ValueKind != JsonValueKind.Array) throw new IOException("Unexpected account list.");
        if (accounts.GetArrayLength() > 1) throw new IOException("The Signal data directory contains multiple accounts.");
        Number = accounts.EnumerateArray().Select(a => a.GetProperty("number").GetString()).SingleOrDefault();
        return Number is not null;
    }
    public async Task<string> StartLinkAsync(CancellationToken token)
    {
        var result = await connection.CallAsync("startLink", cancellationToken: token);
        string value = result.GetProperty("deviceLinkUri").GetString() ?? "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "sgnl" || uri.Host != "linkdevice" || value.Length > 512)
            throw new IOException("Unexpected Signal link response.");
        return value;
    }
    public async Task FinishLinkAsync(string deviceLinkUri, CancellationToken token)
    {
        await connection.CallAsync("finishLink", new { deviceLinkUri, deviceName = "Gather" }, token, TimeSpan.FromMinutes(2));
        if (!await RestoreAsync(token)) throw new IOException("Signal did not finish linking.");
    }
    private object AccountParameters => new { account = Number ?? throw new InvalidOperationException("Link Signal first.") };
    public Task<JsonElement> SubscribeAsync(CancellationToken token) => connection.CallAsync("subscribeReceive", AccountParameters, token);
    public Task<JsonElement> ContactsAsync(CancellationToken token) => connection.CallAsync("listContacts", AccountParameters, token);
    public Task<JsonElement> GroupsAsync(CancellationToken token) => connection.CallAsync("listGroups", AccountParameters, token);
    public async Task<byte[]?> AvatarAsync(string recipient, bool group, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(recipient)) return null;
        string account = Number ?? throw new InvalidOperationException("Link Signal first.");
        async Task<byte[]?> Read(string kind)
        {
            try
            {
                var parameters = new Dictionary<string, object> { ["account"] = account, [kind] = recipient };
                return SignalAvatar.Decode(await connection.CallAsync("getAvatar", parameters, token, TimeSpan.FromSeconds(15)));
            }
            catch (SignalRpcException) { return null; } // An absent/private avatar is ordinary.
        }
        if (group) return await Read("groupId");
        byte[]? profile = await Read("profile");
        if (profile is not null) return profile;
        // Explicit recipients refresh their cached profile in signal-cli. This
        // also helps existing accounts created while avatar receiving was off.
        try { await connection.CallAsync("listContacts", new { account, recipient = new[] { recipient } }, token, TimeSpan.FromSeconds(15)); }
        catch (SignalRpcException) { }
        return await Read("profile") ?? await Read("contact");
    }
    public async Task<long> SendAsync(string destination, bool group, string text, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 10000) throw new ArgumentException("Enter a message up to 10,000 characters.");
        if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Choose a conversation.");
        var parameters = new Dictionary<string, object> { ["account"] = Number ?? throw new InvalidOperationException("Link Signal first."), ["message"] = text };
        parameters[group ? "groupId" : "recipient"] = group ? destination : new[] { destination };
        var result = await connection.CallAsync("send", parameters, token);
        return result.GetProperty("timestamp").GetInt64();
    }
}
