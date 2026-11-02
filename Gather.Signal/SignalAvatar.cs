using System.Text.Json;

namespace Gather.Signal;

public static class SignalAvatar
{
    public const int MaximumBytes = 2 * 1024 * 1024;

    public static byte[]? Decode(JsonElement result)
    {
        string? data = result.String("data");
        if (string.IsNullOrEmpty(data) || data.Length > ((MaximumBytes + 2) / 3) * 4) return null;
        try
        {
            byte[] bytes = Convert.FromBase64String(data);
            if (bytes.Length == 0 || bytes.Length > MaximumBytes) return null;
            return bytes;
        }
        catch (FormatException) { return null; }
    }
}
