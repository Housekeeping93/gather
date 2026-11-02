namespace Gather.Core;

// A received alert is evidence of activity, not an unread-message count.
public sealed class UnreadState
{
    public int? Count
    {
        get; private set;
    }
    public bool Activity
    {
        get; private set;
    }
    public BadgeValue Badge => new(Math.Max(0, Count ?? 0), Activity && Count is not > 0);
    public void Receive() => Activity = true;
    public void SetCount(int count) { Count = Math.Max(0, count); Activity = false; }
    public void Acknowledge() => Activity = false;
    public void UpdateTitle(string title)
    {
        var count = NavigationPolicy.UnreadCount(title);
        // Providers that remove their numeric prefix when read should clear it.
        if (count != null || Count != null)
        {
            Count = count ?? 0;
            Activity = false;
        }
    }
}

public readonly record struct BadgeValue(long Count, bool Unknown)
{
    public bool HasActivity => Count > 0 || Unknown;
    public string Label(string mode) => !HasActivity || mode == "Off" ? ""
        : mode == "Dot" || Count == 0 ? "•"
        : Count > 99 ? "99+" : Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + (Unknown ? "+" : "");
    public static BadgeValue Combine(IEnumerable<BadgeValue> badges)
    {
        long count = 0;
        bool unknown = false;
        foreach (var badge in badges)
        {
            count += Math.Clamp(badge.Count, 0, 999999);
            unknown |= badge.Unknown;
        }
        return new(count, unknown);
    }
}
