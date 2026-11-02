using Gather.Core;

internal static class PrivacyChecks
{
    public static void Run(Action<string, Action> check)
    {
        static void Expect(bool value) { if (!value) throw new Exception("Privacy/shortcut assertion failed."); }
        check("Privacy shortcut defaults and unsupported persisted choices normalize safely", () =>
        {
            var state = new Settings();
            Expect(!state.PrivacyCovered && state.PrivacyShortcut == "Ctrl+Shift+P");
            foreach (var invalid in new[] { null, "", "Alt+F", "Ctrl+Shift+F", "Ctrl+K" })
            {
                state.PrivacyShortcut = invalid!; LayoutActions.Normalize(state);
                Expect(state.PrivacyShortcut == "Ctrl+Shift+P");
            }
        });
        check("Privacy shortcuts reserve only the configured exact chord", () =>
        {
            foreach (var choice in PrivacyCurtain.Shortcuts)
            foreach (var candidate in PrivacyCurtain.Shortcuts)
            foreach (bool control in new[] { false, true })
            foreach (bool shift in new[] { false, true })
            foreach (bool alt in new[] { false, true })
                Expect((ShellShortcuts.Resolve(candidate[^1], control, shift, alt, choice) == ShellShortcut.PrivacyCurtain)
                    == (choice == candidate && control && shift && !alt));
        });
        check("Alt F and legacy split shortcut do not intercept typing, find or AltGr", () =>
        {
            Expect(ShellShortcuts.Resolve('F', false, false, true) == ShellShortcut.SplitView);
            Expect(ShellShortcuts.Resolve('S', true, true, false) == ShellShortcut.SplitView);
            Expect(ShellShortcuts.Resolve('F', true, true, false) == ShellShortcut.FocusView);
            Expect(ShellShortcuts.Resolve('F', true, false, false) == ShellShortcut.None);
            Expect(ShellShortcuts.Resolve('F', true, false, true) == ShellShortcut.None);
            Expect(ShellShortcuts.Resolve('F', false, true, true) == ShellShortcut.None);
            Expect(ShellShortcuts.Resolve('F', false, false, false) == ShellShortcut.None);
        });
        check("Curtain and shortcut survive restart without altering layouts or account policies", () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "Gather-privacy-" + Guid.NewGuid().ToString("N"));
            try
            {
                var a = new Account { ProviderId = "slack", Muted = true, Zoom = 1.25 };
                var b = new Account { ProviderId = "telegram" };
                var state = new Settings { Accounts = [a, b], PrivacyCovered = true, PrivacyShortcut = "Ctrl+Shift+L",
                    FocusView = true, HidePreviews = false, Layout = new() { Left = a.Id, Right = b.Id, Split = true, FocusRight = true, Ratio = .35 } };
                var store = new SettingsStore(dir); store.Save(state);
                var read = store.Load();
                Expect(read.PrivacyCovered && read.PrivacyShortcut == "Ctrl+Shift+L" && read.FocusView && !read.HidePreviews);
                Expect(read.Layout.Split && read.Layout.FocusRight && read.Layout.Ratio == .35 && read.Layout.Right == b.Id);
                Expect(read.Accounts[0].Muted && read.Accounts[0].Zoom == 1.25 && read.Accounts[0].ProfileName == a.ProfileName);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        });
    }
}
