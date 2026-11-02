namespace Gather.Core;

public sealed class QuietSettings
{
    public bool ScheduleEnabled { get; set; }
    public int StartMinute { get; set; } = 1320;
    public int EndMinute { get; set; } = 420;
    // Sunday is bit 0; selected days are the days the occurrence starts.
    public int Days { get; set; } = 127;
    public List<string> ExcludedAccountIds { get; set; } = [];
    public bool HideScheduledBadges { get; set; }
    public string? SkippedOccurrence { get; set; }
    public DateTimeOffset? FocusUntil { get; set; }
    public bool SilenceFocusAudio { get; set; } = true;
    public bool HideFocusBadges { get; set; }
    public QuietSettings Copy() => new()
    {
        ScheduleEnabled = ScheduleEnabled, StartMinute = StartMinute, EndMinute = EndMinute,
        Days = Days, ExcludedAccountIds = [.. ExcludedAccountIds],
        HideScheduledBadges = HideScheduledBadges, SkippedOccurrence = SkippedOccurrence,
        FocusUntil = FocusUntil, SilenceFocusAudio = SilenceFocusAudio, HideFocusBadges = HideFocusBadges
    };
}
public sealed record QuietOccurrence(string Id, DateTimeOffset Start, DateTimeOffset End);
public readonly record struct QuietEffect(bool SuppressNotifications, bool MuteAudio, bool HideBadges);
public sealed record QuietState(QuietOccurrence? Occurrence, bool ScheduleActive, DateTimeOffset? FocusUntil)
{
    public bool FocusActive => FocusUntil != null;
    public QuietEffect ForAccount(QuietSettings settings, Account account)
    {
        bool scheduled = ScheduleActive && !settings.ExcludedAccountIds.Contains(account.Id);
        return new(account.Muted || scheduled || FocusActive,
            account.Muted || scheduled || (FocusActive && settings.SilenceFocusAudio),
            (scheduled && settings.HideScheduledBadges) || (FocusActive && settings.HideFocusBadges));
    }
}
public static class QuietPolicy
{
    public static string? ValidateSchedule(QuietSettings settings)
    {
        if (settings.StartMinute is < 0 or >= 1440 || settings.EndMinute is < 0 or >= 1440)
            return "Choose valid start and end times.";
        if (settings.StartMinute == settings.EndMinute) return "Choose different start and end times.";
        if ((settings.Days & 127) == 0) return "Choose at least one starting day.";
        return null;
    }
    public static void Normalize(QuietSettings settings, IEnumerable<Account> accounts)
    {
        settings.Days &= 127;
        if (ValidateSchedule(settings) != null) settings.ScheduleEnabled = false;
        settings.StartMinute = Math.Clamp(settings.StartMinute, 0, 1439);
        settings.EndMinute = Math.Clamp(settings.EndMinute, 0, 1439);
        var ids = accounts.Select(a => a.Id).ToHashSet();
        settings.ExcludedAccountIds = (settings.ExcludedAccountIds ?? []).Where(ids.Contains).Distinct().ToList();
        if (settings.SkippedOccurrence?.Length > 200) settings.SkippedOccurrence = null;
    }
    public static QuietState Evaluate(QuietSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        QuietOccurrence? occurrence = null;
        if (settings.ScheduleEnabled && ValidateSchedule(settings) == null)
        {
            var today = TimeZoneInfo.ConvertTime(now, zone).Date;
            foreach (var date in new[] { today.AddDays(-1), today })
            {
                if ((settings.Days & (1 << (int)date.DayOfWeek)) == 0) continue;
                var start = ResolveBoundary(date.AddMinutes(settings.StartMinute), zone, false);
                var end = ResolveBoundary(date.AddDays(settings.EndMinute < settings.StartMinute ? 1 : 0).AddMinutes(settings.EndMinute), zone, true);
                if (now >= start && now < end)
                {
                    occurrence = new($"{zone.Id}|{date:yyyy-MM-dd}|{settings.StartMinute}|{settings.EndMinute}", start, end);
                    break;
                }
            }
        }
        return new(occurrence, occurrence != null && occurrence.Id != settings.SkippedOccurrence,
            settings.FocusUntil is { } until && until > now ? until : null);
    }
    private static DateTimeOffset ResolveBoundary(DateTime local, TimeZoneInfo zone, bool end)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        // One continuous occurrence across the repeated hour: earliest start, latest end.
        var offset = zone.IsAmbiguousTime(local)
            ? (end ? zone.GetAmbiguousTimeOffsets(local).Min() : zone.GetAmbiguousTimeOffsets(local).Max())
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
