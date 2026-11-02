using Gather.Core;

internal static class QuickSwitchChecks
{
    public static void Run(Action<string, Action> check)
    {
        static void Expect(bool value) { if (!value) throw new Exception("Quick switch assertion failed."); }
        var personal = new Account { ProviderId = "telegram", Name = "Personal", Muted = true };
        var work = new Account { ProviderId = "telegram", Name = "Office" };
        var signal = new Account { ProviderId = "signal", Name = "Family" };
        Settings State() => new() { Accounts = [personal, work, signal] };
        check("Quick switch matches provider and account words without changing accounts", () =>
        {
            var state = State();
            Expect(QuickSwitch.Search(state, "TELEgram").Count == 2);
            Expect(QuickSwitch.Search(state, "  personal  TELEgram ").Single() == personal);
            Expect(QuickSwitch.Search(state, "family").Single() == signal);
            Expect(QuickSwitch.Search(state, "personal signal").Count == 0);
            Expect(personal.Muted && state.Accounts[0].ProfileName == personal.ProfileName);
        });
        check("Quick switch recent order is distinct, stable and falls back to sidebar order", () =>
        {
            var state = State();
            Expect(QuickSwitch.Search(state, "").SequenceEqual(state.Accounts));
            QuickSwitch.Remember(state, work.Id); QuickSwitch.Remember(state, signal.Id);
            Expect(!QuickSwitch.Remember(state, signal.Id));
            Expect(QuickSwitch.Search(state, " ").SequenceEqual(new[] { signal, work, personal }));
            Expect(QuickSwitch.Search(state, "telegram").SequenceEqual(new[] { work, personal }));
            Expect(!QuickSwitch.Remember(state, "removed"));
        });
        check("Quick switch recent history survives restart and prunes removed accounts", () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "Gather-quick-switch-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SettingsStore(dir, "settings-signal-preview.json");
                var state = State(); QuickSwitch.Remember(state, work.Id); store.Save(state);
                var read = store.Load();
                Expect(QuickSwitch.Search(read, null).First().Id == work.Id);
                read.Accounts.RemoveAll(a => a.Id == work.Id); LayoutActions.Normalize(read);
                Expect(read.RecentAccountIds.Count == 0 && read.Accounts[0].ProfileName == personal.ProfileName);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        });
        check("Quick switch normalizes legacy, null, duplicate and oversized recent history", () =>
        {
            var state = State(); state.RecentAccountIds = null!; LayoutActions.Normalize(state);
            Expect(state.RecentAccountIds.Count == 0);
            state.Accounts = Enumerable.Range(0, 30).Select(i => new Account { ProviderId = "signal", Name = "Account " + i }).ToList();
            state.RecentAccountIds = state.Accounts.Select(a => a.Id).Concat(state.Accounts.Select(a => a.Id)).Append("stale").ToList();
            LayoutActions.Normalize(state);
            Expect(state.RecentAccountIds.Count == QuickSwitch.RecentLimit);
            QuickSwitch.Remember(state, state.Accounts[^1].Id);
            Expect(state.RecentAccountIds[0] == state.Accounts[^1].Id && state.RecentAccountIds.Count == QuickSwitch.RecentLimit);
        });
        check("Quick switch target selection preserves existing panes and focuses replacements", () =>
        {
            var layout = new Layout { Left = personal.Id, Right = work.Id, Split = true, FocusRight = true, Ratio = .4 };
            LayoutActions.Select(layout, personal.Id);
            Expect(!layout.FocusRight && layout.Right == work.Id && layout.Ratio == .4);
            LayoutActions.Select(layout, signal.Id);
            Expect(layout.Left == signal.Id && layout.Right == work.Id && !layout.FocusRight);
            LayoutActions.Select(layout, work.Id);
            Expect(layout.FocusRight && layout.Left == signal.Id && layout.Right == work.Id);
            LayoutActions.Collapse(layout); LayoutActions.Select(layout, personal.Id);
            Expect(!layout.Split && layout.Left == personal.Id && !layout.FocusRight);
        });
        check("Quick switch supports empty accounts and Unicode account names", () =>
        {
            Expect(QuickSwitch.Search(new Settings(), "signal").Count == 0);
            var state = new Settings { Accounts = [new() { ProviderId = "signal", Name = "Семейство" }] };
            Expect(QuickSwitch.Search(state, "СЕМЕЙ").Count == 1);
        });
    }
}
