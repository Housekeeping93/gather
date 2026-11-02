using Gather.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gather.App;
public sealed partial class MainWindow
{
    private DispatcherTimer? _quietTimer;
    private bool _suspending;
    private string? _quietLabel;
    private Func<DateTimeOffset> _quietClock = () => DateTimeOffset.UtcNow;
    private TimeZoneInfo QuietZone => TimeZoneInfo.Local;
    private QuietState CurrentQuiet => QuietPolicy.Evaluate(_state.Quiet, _quietClock(), QuietZone);
    internal QuietEffect QuietFor(Account account)
    {
        var effect = CurrentQuiet.ForAccount(_state.Quiet, account);
        return _suspending ? effect with { MuteAudio = true, SuppressNotifications = true } : effect;
    }
    private void InitializeQuiet()
    {
        QuietButton.Click += (_, _) => OpenQuietMenu();
        _quietTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _quietTimer.Tick += (_, _) => RefreshQuiet();
        _quietTimer.Start();
        Closed += (_, _) => _quietTimer.Stop();
        RefreshQuiet();
    }
    private void QuietSystemChanged(bool suspending)
    {
        _suspending = suspending;
        TimeZoneInfo.ClearCachedData();
        RefreshQuiet();
    }
    private void RefreshQuiet()
    {
        if (_quitting) return;
        var now = _quietClock();
        if (_state.Quiet.FocusUntil is { } expiry && expiry <= now)
        {
            _state.Quiet.FocusUntil = null;
            Save();
        }
        var state = QuietPolicy.Evaluate(_state.Quiet, now, QuietZone);
        foreach (var view in _views.Values.ToArray())
        {
            try { view.SetAudioMuted(_suspending || state.ForAccount(_state.Quiet, view.Account).MuteAudio); }
            catch (Exception ex) { Diagnostic("quiet-audio", ex); }
        }
        UpdateStatus();
    }
    private void UpdateQuietIndicator(QuietState state)
    {
        string schedule = state.ScheduleActive ? $"Do Not Disturb until {TimeZoneInfo.ConvertTime(state.Occurrence!.End, QuietZone):HH:mm}" : "";
        int minutes = state.FocusUntil is { } end ? Math.Max(1, (int)Math.Ceiling((end - _quietClock()).TotalMinutes)) : 0;
        string focus = state.FocusActive ? $"Focus · {minutes} min" : "";
        string label = state.ScheduleActive && state.FocusActive ? $"{schedule} · {focus}" :
            state.ScheduleActive ? schedule : state.FocusActive ? focus :
            state.Occurrence != null ? "Quiet period skipped" : "Focus & DND";
        if (_quietLabel == label) return;
        _quietLabel = label;
        string display = state.ScheduleActive ? (state.FocusActive ? $"{minutes}m focus · DND {TimeZoneInfo.ConvertTime(state.Occurrence!.End, QuietZone):HH:mm}" : $"DND until {TimeZoneInfo.ConvertTime(state.Occurrence!.End, QuietZone):HH:mm}") : label;
        QuietButton.Content = new TextBlock { Text = display, MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
        ToolTipService.SetToolTip(QuietButton, label + " · Focus timer, quiet hours and exceptions");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(QuietButton, label);
        QuietButton.Background = state.ScheduleActive || state.FocusActive
            ? Brush(IsDark ? "#203C35" : "#DFF5EC") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _native.SetQuietStatus(label);
    }
    private bool CommitQuiet(QuietSettings draft)
    {
        var previous = _state.Quiet;
        _state.Quiet = draft;
        try { _store.Save(_state); }
        catch (Exception ex)
        {
            _state.Quiet = previous;
            Diagnostic("quiet-save", ex);
            ShowNotice("Quiet settings not saved", "Check folder access and try again. Your previous settings are still active.", InfoBarSeverity.Error);
            return false;
        }
        RefreshQuiet();
        return true;
    }
    private bool StartFocus(int minutes, bool? silence = null, bool? hideBadges = null)
    {
        var draft = _state.Quiet.Copy();
        draft.FocusUntil = _quietClock().AddMinutes(Math.Clamp(minutes, 1, 1440));
        draft.SilenceFocusAudio = silence ?? draft.SilenceFocusAudio;
        draft.HideFocusBadges = hideBadges ?? draft.HideFocusBadges;
        return CommitQuiet(draft);
    }
    private void OpenQuietMenu()
    {
        RefreshQuiet();
        var menu = new MenuFlyout();
        MenuItem(menu, "Focus for 25 minutes", () => StartFocus(25));
        MenuItem(menu, "Focus for 50 minutes", () => StartFocus(50));
        MenuItem(menu, "Custom focus & options…", async () => await ShowFocusDialog());
        var state = CurrentQuiet;
        if (state.FocusActive)
        {
            MenuItem(menu, "Add 15 minutes", () =>
            {
                var draft = _state.Quiet.Copy();
                var now = _quietClock();
                draft.FocusUntil = now.AddMinutes(Math.Min(1440, Math.Max(0, ((CurrentQuiet.FocusUntil ?? now) - now).TotalMinutes) + 15));
                CommitQuiet(draft);
            });
            MenuItem(menu, "End focus session", () => { var draft = _state.Quiet.Copy(); draft.FocusUntil = null; CommitQuiet(draft); });
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        if (state.Occurrence != null)
            MenuItem(menu, state.ScheduleActive ? "Skip this quiet period" : "Resume this quiet period", () =>
            {
                var current = CurrentQuiet;
                if (current.Occurrence == null) return;
                var draft = _state.Quiet.Copy();
                draft.SkippedOccurrence = current.ScheduleActive ? current.Occurrence.Id : null;
                CommitQuiet(draft);
            });
        MenuItem(menu, "Quiet hours & exceptions…", async () => await ShowQuietSettings());
        menu.ShowAt(QuietButton);
    }
    private async Task ShowFocusDialog()
    {
        var minutes = new NumberBox { Name = "FocusMinutes", Header = "Duration in minutes", Minimum = 1, Maximum = 1440, Value = 25, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var audio = new CheckBox { Name = "FocusAudio", Content = "Silence service audio", IsChecked = _state.Quiet.SilenceFocusAudio };
        var badges = new CheckBox { Name = "FocusBadges", Content = "Hide unread badges", IsChecked = _state.Quiet.HideFocusBadges };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(Text("Pause notifications from every Gather account. Scheduled quiet-hour exceptions do not bypass a focus timer.", 12));
        content.Children.Add(minutes); content.Children.Add(audio); content.Children.Add(badges);
        var dialog = Dialog("Focus session", content, "Start focus", "Cancel");
        dialog.PrimaryButtonClick += (_, e) =>
        {
            if (!double.IsFinite(minutes.Value) || minutes.Value < 1 || minutes.Value > 1440)
            { e.Cancel = true; minutes.Header = "Enter 1–1440 minutes"; return; }
            e.Cancel = !StartFocus((int)Math.Ceiling(minutes.Value), audio.IsChecked == true, badges.IsChecked == true);
        };
        await ShowDialog(dialog);
    }
    private async Task ShowQuietSettings()
    {
        var draft = _state.Quiet.Copy();
        var enabled = new ToggleSwitch { Name = "QuietEnabled", Header = "Scheduled Do Not Disturb", IsOn = draft.ScheduleEnabled };
        var start = new TimePicker { Name = "QuietStart", Header = "From", ClockIdentifier = "24HourClock", Time = TimeSpan.FromMinutes(draft.StartMinute) };
        var end = new TimePicker { Name = "QuietEnd", Header = "Until", ClockIdentifier = "24HourClock", Time = TimeSpan.FromMinutes(draft.EndMinute) };
        double contentWidth = Math.Clamp(Root.ActualWidth - 100, 280, 500);
        var times = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        if (contentWidth >= 500)
        {
            times.ColumnDefinitions.Add(new ColumnDefinition()); times.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(end, 1);
        }
        else
        {
            times.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            times.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(end, 1);
        }
        start.HorizontalAlignment = end.HorizontalAlignment = HorizontalAlignment.Stretch;
        times.Children.Add(start); times.Children.Add(end);
        int dayColumns = contentWidth >= 420 ? 7 : 4;
        var days = new Grid { ColumnSpacing = 2, RowSpacing = 4 };
        for (int i = 0; i < dayColumns; i++) days.ColumnDefinitions.Add(new ColumnDefinition());
        days.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        days.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var dayBoxes = new List<(int Bit, CheckBox Box)>();
        string[] names = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        for (int i = 0; i < 7; i++)
        {
            int bit = 1 << ((i + 1) % 7);
            var day = new CheckBox { Name = "QuietDay" + i, Content = names[i], IsChecked = (draft.Days & bit) != 0, MinWidth = 0, FontSize = 12 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(day, ((DayOfWeek)((i + 1) % 7)).ToString());
            Grid.SetColumn(day, i % dayColumns); Grid.SetRow(day, i / dayColumns); days.Children.Add(day); dayBoxes.Add((bit, day));
        }
        var hide = new CheckBox { Name = "QuietBadges", Content = "Also hide badges for muted services", IsChecked = draft.HideScheduledBadges };
        var content = new StackPanel { Spacing = 12, Width = contentWidth };
        content.Children.Add(enabled);
        content.Children.Add(Text("Silence notifications and all service audio during quiet hours. Your accounts stay connected.", 12));
        content.Children.Add(times);
        content.Children.Add(Text("Starting days", 13, true)); content.Children.Add(days);
        content.Children.Add(Text("Uses your computer's local time. For 22:00–07:00, Monday includes Tuesday morning.", 12));
        content.Children.Add(hide);
        content.Children.Add(Text("Allow during scheduled DND", 15, true));
        content.Children.Add(Text("Selected accounts keep their usual settings. Manual mutes and focus timers still apply.", 12));
        var exceptions = new List<(Account Account, CheckBox Box)>();
        foreach (var account in _state.Accounts)
        {
            var box = new CheckBox { Name = "QuietException" + exceptions.Count,
                Content = new TextBlock { Text = $"{account.Name} · {Providers.Find(account.ProviderId)!.Name}", TextWrapping = TextWrapping.Wrap },
                IsChecked = draft.ExcludedAccountIds.Contains(account.Id) };
            exceptions.Add((account, box)); content.Children.Add(box);
        }
        if (exceptions.Count == 0) content.Children.Add(Text("Add services to choose exceptions. New accounts are muted during quiet hours by default.", 12));
        var error = new InfoBar { Name = "QuietError", IsClosable = false, Severity = InfoBarSeverity.Error };
        content.Children.Insert(0, error);
        var dialog = Dialog("Quiet hours & exceptions", new ScrollViewer { Content = content, MaxHeight = Math.Max(220, Math.Min(460, Root.ActualHeight - 220)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, "Save", "Cancel");
        dialog.PrimaryButtonClick += (_, e) =>
        {
            draft.ScheduleEnabled = enabled.IsOn;
            draft.StartMinute = (int)start.Time.TotalMinutes; draft.EndMinute = (int)end.Time.TotalMinutes;
            draft.Days = dayBoxes.Where(x => x.Box.IsChecked == true).Aggregate(0, (mask, x) => mask | x.Bit);
            string? problem = draft.ScheduleEnabled ? QuietPolicy.ValidateSchedule(draft) : null;
            if (problem != null) { error.Message = problem; error.IsOpen = true; e.Cancel = true; return; }
            draft.ExcludedAccountIds = exceptions.Where(x => x.Box.IsChecked == true).Select(x => x.Account.Id).ToList();
            draft.HideScheduledBadges = hide.IsChecked == true;
            draft.FocusUntil = _state.Quiet.FocusUntil;
            draft.SilenceFocusAudio = _state.Quiet.SilenceFocusAudio;
            draft.HideFocusBadges = _state.Quiet.HideFocusBadges;
            if (draft.StartMinute != _state.Quiet.StartMinute || draft.EndMinute != _state.Quiet.EndMinute ||
                draft.Days != _state.Quiet.Days || draft.ScheduleEnabled != _state.Quiet.ScheduleEnabled)
                draft.SkippedOccurrence = null;
            e.Cancel = !CommitQuiet(draft);
        };
        await ShowDialog(dialog);
    }
    private async void QuietSettings_Click(object sender, RoutedEventArgs args) => await ShowQuietSettings();
}
