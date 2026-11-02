using Gather.Core;

internal static class ShellAppearanceChecks
{
    public static void Run(Action<string, Action> check)
    {
        static void Expect(bool value) { if (!value) throw new Exception("Shell appearance assertion failed."); }
        check("Shell defaults and invalid legacy density preserve the compact layout", () =>
        {
            var state = new Settings(); LayoutActions.Normalize(state);
            Expect(state.ShellDensity == "Compact" && !state.FocusView);
            var m = ShellAppearance.For(state.ShellDensity, false);
            Expect(m.TitleHeight == 32 && m.ToolbarHeight == 44 && m.RailWidth == 72 && m.PaneHeaderHeight == 40);
            state.ShellDensity = "unknown"; LayoutActions.Normalize(state); Expect(state.ShellDensity == "Compact");
            state.ShellDensity = null!; LayoutActions.Normalize(state); Expect(state.ShellDensity == "Compact");
        });
        check("Comfortable enlarges shell controls while focus mode restores either preset", () =>
        {
            var compact = ShellAppearance.For("Compact", false); var comfortable = ShellAppearance.For("Comfortable", false);
            Expect(comfortable.ButtonHeight > compact.ButtonHeight && comfortable.TileSize > compact.TileSize);
            Expect(comfortable.PaneHeaderHeight > compact.PaneHeaderHeight && comfortable.OuterSpacing > compact.OuterSpacing);
            foreach (var density in new[] { "Compact", "Comfortable" })
            {
                var normal = ShellAppearance.For(density, false); var focus = ShellAppearance.For(density, true);
                Expect(focus.RailWidth == 0 && focus.PaneHeaderHeight == 0 && focus.TitleHeight == normal.TitleHeight);
                Expect(focus.ButtonHeight == normal.ButtonHeight && focus.ToolbarHeight > focus.ButtonHeight);
                Expect(ShellAppearance.For(density, false) == normal);
            }
        });
        check("Shell preferences survive restart without changing zoom, profile, layout or mute", () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "Gather-shell-" + Guid.NewGuid().ToString("N"));
            try
            {
                var account = new Account { ProviderId = "signal", Name = "Family", Zoom = 1.35, Muted = true };
                var state = new Settings { Accounts = [account], ShellDensity = "Comfortable", FocusView = true };
                state.Layout.Left = account.Id; state.Layout.Ratio = .35;
                var store = new SettingsStore(dir, "settings-signal-preview.json"); store.Save(state);
                var read = store.Load();
                Expect(read.ShellDensity == "Comfortable" && read.FocusView && read.Layout.Ratio == .35);
                Expect(read.Accounts[0].Zoom == 1.35 && read.Accounts[0].Muted && read.Accounts[0].ProfileName == account.ProfileName);
                Expect(!read.Quiet.ScheduleEnabled && read.Quiet.FocusUntil == null);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        });
        check("Shell shortcut routing distinguishes focus view from quick switch and typing", () =>
        {
            Expect(ShellShortcuts.Resolve(0x46, true, true, false) == ShellShortcut.FocusView);
            Expect(ShellShortcuts.Resolve(0x46, true, false, false) == ShellShortcut.None);
            Expect(ShellShortcuts.Resolve(0x46, true, true, true) == ShellShortcut.None);
            Expect(ShellShortcuts.Resolve(0x46, false, true, false) == ShellShortcut.None);
            Expect(ShellShortcuts.Resolve(0x4B, true, false, false) == ShellShortcut.QuickSwitch);
            Expect(ShellShortcuts.Resolve(0x4B, true, true, false) == ShellShortcut.None);
            Expect(ShellShortcuts.Resolve(9, true, false, false) == ShellShortcut.Next);
            Expect(ShellShortcuts.Resolve(9, true, true, false) == ShellShortcut.Previous);
        });
    }
}
