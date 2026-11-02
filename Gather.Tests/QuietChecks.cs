using Gather.Core;

internal static class QuietChecks
{
    public static void Run(Action<string, Action> check)
    {
        static void Expect(bool pass) { if (!pass) throw new Exception("Quiet policy assertion failed"); }
        static DateTimeOffset At(string s) => DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        static QuietSettings Schedule() => new() { ScheduleEnabled = true };
        static QuietState Eval(QuietSettings q, string time) => QuietPolicy.Evaluate(q, At(time), TimeZoneInfo.Utc);
        var account = new Account { ProviderId = "telegram" };
        var other = new Account { ProviderId = "telegram" };
        check("Quiet defaults preserve normal delivery and legacy account mute", () =>
        {
            var q = new QuietSettings();
            Expect(!Eval(q, "2026-11-02T23:00Z").ForAccount(q, account).SuppressNotifications);
            var muted = new Account { ProviderId = "signal", Muted = true };
            Expect(Eval(q, "2026-11-02T23:00Z").ForAccount(q, muted).MuteAudio);
        });
        check("Quiet overnight schedule has inclusive start and exclusive end", () =>
        {
            var q = Schedule();
            Expect(!Eval(q, "2026-11-02T21:59:59Z").ScheduleActive);
            Expect(Eval(q, "2026-11-02T22:00Z").ScheduleActive);
            Expect(Eval(q, "2026-11-03T06:59:59Z").ScheduleActive);
            Expect(!Eval(q, "2026-11-03T07:00Z").ScheduleActive);
        });
        check("Quiet weekdays refer to start day including the next morning", () =>
        {
            var q = Schedule(); q.Days = 1 << (int)DayOfWeek.Monday;
            Expect(Eval(q, "2026-11-03T06:00Z").ScheduleActive);
            Expect(!Eval(q, "2026-11-03T22:00Z").ScheduleActive);
            Expect(!Eval(q, "2026-11-02T06:00Z").ScheduleActive);
        });
        check("Quiet daytime schedules and Sunday overnight wrap correctly", () =>
        {
            var q = Schedule(); q.StartMinute = 9 * 60; q.EndMinute = 17 * 60;
            Expect(Eval(q, "2026-11-02T12:00Z").ScheduleActive);
            Expect(!Eval(q, "2026-11-02T17:00Z").ScheduleActive);
            q.StartMinute = 1320; q.EndMinute = 420; q.Days = 1;
            Expect(Eval(q, "2026-11-02T06:00Z").ScheduleActive);
        });
        check("Quiet exceptions apply to one account, not its provider or new accounts", () =>
        {
            var q = Schedule(); q.ExcludedAccountIds.Add(account.Id);
            var s = Eval(q, "2026-11-02T23:00Z");
            Expect(!s.ForAccount(q, account).SuppressNotifications);
            Expect(s.ForAccount(q, other).SuppressNotifications && s.ForAccount(q, other).MuteAudio);
        });
        check("Quiet exception cannot override manual mute or focus", () =>
        {
            var q = Schedule(); q.ExcludedAccountIds.Add(account.Id);
            account.Muted = true;
            Expect(Eval(q, "2026-11-02T23:00Z").ForAccount(q, account).MuteAudio);
            account.Muted = false; q.FocusUntil = At("2026-11-03T01:00Z");
            Expect(Eval(q, "2026-11-02T23:00Z").ForAccount(q, account).SuppressNotifications);
        });
        check("Quiet focus permits audio and badges independently of notifications", () =>
        {
            var q = new QuietSettings { FocusUntil = At("2026-11-02T23:50Z"), SilenceFocusAudio = false, HideFocusBadges = false };
            var effect = Eval(q, "2026-11-02T23:00Z").ForAccount(q, account);
            Expect(effect.SuppressNotifications && !effect.MuteAudio && !effect.HideBadges);
            q.HideFocusBadges = true;
            Expect(Eval(q, "2026-11-02T23:00Z").ForAccount(q, account).HideBadges);
        });
        check("Quiet ending one overlapping mode keeps the other mode effective", () =>
        {
            var q = Schedule(); q.FocusUntil = At("2026-11-03T07:15Z");
            Expect(Eval(q, "2026-11-03T07:00Z").ForAccount(q, account).SuppressNotifications);
            Expect(!Eval(q, "2026-11-03T07:15Z").ForAccount(q, account).SuppressNotifications);
            q.FocusUntil = At("2026-11-02T23:30Z");
            Expect(Eval(q, "2026-11-02T23:30Z").ForAccount(q, account).MuteAudio);
        });
        check("Quiet skip covers exactly one occurrence and preserves focus", () =>
        {
            var q = Schedule(); q.SkippedOccurrence = Eval(q, "2026-11-02T23:00Z").Occurrence!.Id;
            Expect(!Eval(q, "2026-11-03T06:00Z").ScheduleActive);
            Expect(Eval(q, "2026-11-03T23:00Z").ScheduleActive);
            q.FocusUntil = At("2026-11-03T01:00Z");
            Expect(Eval(q, "2026-11-02T23:00Z").ForAccount(q, account).SuppressNotifications);
        });
        check("Quiet hidden badges retain exception visibility and unread state", () =>
        {
            var q = Schedule(); q.HideScheduledBadges = true; q.ExcludedAccountIds.Add(account.Id);
            var s = Eval(q, "2026-11-02T23:00Z");
            var unread = new UnreadState(); unread.SetCount(12);
            Expect(!s.ForAccount(q, account).HideBadges && s.ForAccount(q, other).HideBadges);
            Expect(unread.Count == 12);
            q.FocusUntil = At("2026-11-03T01:00Z"); q.HideFocusBadges = true;
            Expect(Eval(q, "2026-11-02T23:00Z").ForAccount(q, account).HideBadges);
        });
        check("Quiet end preserves changed account preferences without mutating them", () =>
        {
            var q = Schedule();
            account.Muted = true;
            Expect(Eval(q, "2026-11-03T08:00Z").ForAccount(q, account).MuteAudio && account.Muted);
            account.Muted = false;
            Expect(!Eval(q, "2026-11-03T08:00Z").ForAccount(q, account).MuteAudio && !account.Muted);
        });
        check("Quiet invalid schedules are rejected and malformed persisted data is normalized", () =>
        {
            foreach (var q in new[] { new QuietSettings { StartMinute = 420 }, new QuietSettings { Days = 0 }, new QuietSettings { EndMinute = 1440 } })
                Expect(QuietPolicy.ValidateSchedule(q) != null);
            var bad = new QuietSettings { ScheduleEnabled = true, Days = 128, StartMinute = -9, ExcludedAccountIds = null! };
            QuietPolicy.Normalize(bad, [account]);
            Expect(!bad.ScheduleEnabled && bad.StartMinute == 0 && bad.ExcludedAccountIds.Count == 0);
        });
        check("Quiet restart preserves schedule, exceptions, focus and skip", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "Gather-quiet-" + Guid.NewGuid().ToString("N"));
            var q = Schedule(); q.ExcludedAccountIds = [account.Id, account.Id, "missing"];
            q.FocusUntil = At("2026-11-03T01:00Z");
            q.SkippedOccurrence = Eval(q, "2026-11-02T23:00Z").Occurrence!.Id;
            var store = new SettingsStore(dir); store.Save(new Settings { Accounts = [account], Quiet = q });
            var loaded = store.Load();
            Expect(loaded.Quiet.ExcludedAccountIds.SequenceEqual(new[] { account.Id }));
            var s = Eval(loaded.Quiet, "2026-11-02T23:30Z");
            Expect(s.FocusActive && !s.ScheduleActive);
            File.Delete(Path.Combine(dir, "settings.json")); Directory.Delete(dir);
        });
        check("Quiet cold start and wake compute current state without relying on a timer transition", () =>
        {
            var q = Schedule();
            Expect(Eval(q, "2026-11-02T23:30Z").ForAccount(q, account).MuteAudio);
            Expect(!Eval(q, "2026-11-03T09:30Z").ForAccount(q, account).MuteAudio);
        });
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        check("Quiet timezone changes use local wall time", () =>
        {
            var q = Schedule(); var instant = At("2026-11-02T21:30Z");
            Expect(!QuietPolicy.Evaluate(q, instant, TimeZoneInfo.Utc).ScheduleActive);
            Expect(QuietPolicy.Evaluate(q, instant, zone).ScheduleActive);
        });
        check("Quiet spring-forward skipped start begins at first valid local minute", () =>
        {
            var q = Schedule(); q.StartMinute = 150; q.EndMinute = 240;
            Expect(!QuietPolicy.Evaluate(q, At("2026-03-29T00:59:59Z"), zone).ScheduleActive);
            Expect(QuietPolicy.Evaluate(q, At("2026-03-29T01:00Z"), zone).ScheduleActive);
            Expect(!QuietPolicy.Evaluate(q, At("2026-03-29T02:00Z"), zone).ScheduleActive);
        });
        check("Quiet autumn repeated hour is one continuous occurrence", () =>
        {
            var q = Schedule(); q.StartMinute = 150; q.EndMinute = 165;
            var first = QuietPolicy.Evaluate(q, At("2026-10-25T00:30Z"), zone);
            var repeated = QuietPolicy.Evaluate(q, At("2026-10-25T01:10Z"), zone);
            Expect(first.ScheduleActive && repeated.ScheduleActive && first.Occurrence!.Id == repeated.Occurrence!.Id);
            Expect(!QuietPolicy.Evaluate(q, At("2026-10-25T01:45Z"), zone).ScheduleActive);
            q.SkippedOccurrence = first.Occurrence!.Id;
            Expect(!QuietPolicy.Evaluate(q, At("2026-10-25T01:35Z"), zone).ScheduleActive);
        });
        check("Quiet backward clock adjustment re-evaluates schedule without changing account preferences", () =>
        {
            var q = Schedule();
            Expect(Eval(q, "2026-11-02T22:05Z").ScheduleActive);
            Expect(!Eval(q, "2026-11-02T21:55Z").ScheduleActive);
            Expect(!account.Muted);
        });
    }
}
