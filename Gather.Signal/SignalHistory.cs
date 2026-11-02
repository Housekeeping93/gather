using System.Text.Json;

namespace Gather.Signal;

public sealed class SignalMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Sender { get; set; } = "";
    public string Text { get; set; } = "";
    public long Timestamp { get; set; }
    public bool Outgoing { get; set; }
    public bool Unread { get; set; }
    public string Status { get; set; } = "Received";
}
public sealed class SignalConversation
{
    public string Id { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Group { get; set; }
    public bool Blocked { get; set; }
    public int ExpirationSeconds { get; set; }
    public string Draft { get; set; } = "";
    public List<SignalMessage> Messages { get; set; } = [];
    public HashSet<string> Removed { get; set; } = [];
    public int UnreadCount => Messages.Count(m => m.Unread);
    public override string ToString() => Name + (UnreadCount > 0 ? $"  ({UnreadCount})" : "");
}
public sealed class SignalHistory
{
    public List<SignalConversation> Conversations { get; set; } = [];
    public string? SelectedId { get; set; }
    public SignalConversation Get(string recipient, bool group, string? name = null)
    {
        string id = (group ? "group:" : "person:") + recipient;
        var conversation = Conversations.FirstOrDefault(c => c.Id == id);
        if (conversation is null)
        {
            conversation = new() { Id = id, Recipient = recipient, Group = group, Name = name ?? recipient };
            Conversations.Add(conversation);
        }
        if (!string.IsNullOrWhiteSpace(name)) conversation.Name = name;
        return conversation;
    }
    public void RecoverInterruptedSends()
    {
        foreach (var message in Conversations.SelectMany(c => c.Messages).Where(m => m.Status == "Sending"))
            message.Status = "Delivery unknown — check before sending again";
    }
    // Returns only a newly received ordinary message suitable for a private notification.
    public (SignalConversation Conversation, SignalMessage Message)? Apply(JsonElement notification, string number)
    {
        if (notification.String("method") != "receive") return null;
        var parameters = notification.Object("params");
        if (parameters.TryGetProperty("result", out var wrapped)) parameters = wrapped;
        string? account = parameters.String("account");
        if (account is not null && account != number) return null;
        var envelope = parameters.Object("envelope");
        var receipt = envelope.Object("receiptMessage");
        if (receipt.TryGetProperty("timestamps", out var timestamps) && timestamps.ValueKind == JsonValueKind.Array)
        {
            // A group receipt from one member does not establish delivery to the whole group.
            string? sender = envelope.String("sourceNumber") ?? envelope.String("sourceUuid") ?? envelope.String("source");
            foreach (var time in timestamps.EnumerateArray())
                if (time.TryGetInt64(out long sentAt))
                    foreach (var m in Conversations.Where(c => !c.Group && c.Recipient == sender).SelectMany(c => c.Messages)
                        .Where(m => m.Outgoing && m.Timestamp == sentAt))
                        if (receipt.Bool("isRead")) m.Status = "Read";
                        else if (receipt.Bool("isDelivery") && m.Status != "Read") m.Status = "Delivered";
            return null;
        }
        bool outgoing = envelope.Object("syncMessage").TryGetProperty("sentMessage", out var data);
        if (!outgoing) data = envelope.Object("dataMessage");
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("timestamp", out var ts) || !ts.TryGetInt64(out long timestamp)) return null;
        string senderId = envelope.String("sourceNumber") ?? envelope.String("sourceUuid") ?? envelope.String("source") ?? "";
        string? recipient = outgoing ? data.String("destinationNumber") ?? data.String("destination") ?? data.String("destinationUuid") : senderId;
        string? groupId = data.Object("groupInfo").String("groupId");
        if (groupId is not null) recipient = groupId;
        if (string.IsNullOrWhiteSpace(recipient)) return null;
        var chat = Get(recipient, groupId is not null, groupId is not null ? null : outgoing ? null : envelope.String("sourceName"));
        if (chat.Blocked) return null;
        string id = (outgoing ? "self" : senderId) + ":" + timestamp;
        var deletion = data.Object("remoteDelete");
        if (deletion.TryGetProperty("timestamp", out var deleted) && deleted.TryGetInt64(out long deletedAt))
        {
            string target = (outgoing ? "self" : senderId) + ":" + deletedAt;
            chat.Removed.Add(target);
            chat.Messages.RemoveAll(m => m.Id == target || (outgoing && m.Outgoing && m.Timestamp == deletedAt));
            return null;
        }
        if (data.TryGetProperty("expiresInSeconds", out var expiration) && expiration.TryGetInt32(out int seconds))
            chat.ExpirationSeconds = Math.Max(0, seconds);
        // This text-only development slice never turns restricted content into ordinary history.
        bool restricted = data.Bool("viewOnce") || chat.ExpirationSeconds > 0;
        if (restricted) return null;
        if (chat.Removed.Contains(id) || chat.Messages.Any(m => m.Id == id || (outgoing && m.Outgoing && m.Timestamp == timestamp))) return null;
        string? text = data.String("message");
        if (string.IsNullOrEmpty(text)) return null; // Attachments/control events are not fabricated as messages.
        var message = new SignalMessage { Id = id, Sender = outgoing ? "You" : envelope.String("sourceName") ?? senderId,
            Text = text, Timestamp = timestamp, Outgoing = outgoing, Unread = !outgoing, Status = outgoing ? "Sent from another device" : "Received" };
        chat.Messages.Add(message);
        if (chat.Messages.Count > 1000) chat.Messages.RemoveRange(0, chat.Messages.Count - 1000);
        return outgoing ? null : (chat, message);
    }
    public void UpdateContacts(JsonElement contacts)
    {
        if (contacts.ValueKind != JsonValueKind.Array) return;
        foreach (var contact in contacts.EnumerateArray())
        {
            string? recipient = contact.String("number") ?? contact.String("uuid");
            if (string.IsNullOrWhiteSpace(recipient)) continue;
            string? name = contact.String("name");
            if (string.IsNullOrWhiteSpace(name)) name = contact.Object("profile").String("givenName");
            var conversation = Get(recipient, false, name);
            conversation.Blocked = contact.Bool("isBlocked");
            conversation.ExpirationSeconds = contact.Int("messageExpirationTime");
        }
    }
    public void UpdateGroups(JsonElement groups)
    {
        if (groups.ValueKind != JsonValueKind.Array) return;
        foreach (var group in groups.EnumerateArray())
        {
            string? id = group.String("id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var conversation = Get(id, true, group.String("name"));
            conversation.Blocked = group.Bool("isBlocked") || !group.Bool("isMember");
            conversation.ExpirationSeconds = group.Int("messageExpirationTime");
        }
    }
}
internal static class JsonFields
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    public static JsonElement Object(this JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : Empty;
    public static string? String(this JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static bool Bool(this JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    public static int Int(this JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : 0;
}
