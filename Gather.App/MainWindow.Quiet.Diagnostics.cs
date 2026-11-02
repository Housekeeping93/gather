using Gather.Core;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
namespace Gather.App;
public sealed partial class MainWindow
{
    private async Task RunQuietSmokeTest()
    {
        try
        {
            if (_state.Accounts.Count != 0) throw new InvalidOperationException("Use a fresh quiet-check profile.");
            var personal = new Account { ProviderId = "telegram", Name = "Personal Telegram", StartConnected = false };
            var work = new Account { ProviderId = "slack", Name = "Work Slack", StartConnected = false };
            _state.Accounts.AddRange([personal, work]); BuildRail();
            var now = new DateTimeOffset(2026, 11, 2, 23, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 2)));
            _quietClock = () => now;
            _state.Theme = "Dark"; ApplyTheme();
            var pending = ShowQuietSettings(); await Task.Delay(350);
            foreach (var picker in DialogElements(_activeDialog!).OfType<TimePicker>())
            {
                var button = DialogElements(picker).OfType<Button>().First(x => x.Name == "FlyoutButton");
                if (button.ActualWidth > picker.ActualWidth + 1) throw new Exception("Time picker is clipped.");
            }
            QuietControl<ToggleSwitch>("QuietEnabled").IsOn = true;
            InvokeSettingsButton("Cancel"); await pending;
            if (_state.Quiet.ScheduleEnabled) throw new Exception("Cancel changed the quiet schedule.");
            pending = ShowQuietSettings(); await Task.Delay(250);
            QuietControl<ToggleSwitch>("QuietEnabled").IsOn = true;
            QuietControl<TimePicker>("QuietEnd").Time = TimeSpan.FromHours(22);
            InvokeSettingsButton("Save"); await Task.Delay(150);
            if (!QuietControl<InfoBar>("QuietError").IsOpen || _state.Quiet.ScheduleEnabled)
                throw new Exception("Equal-time validation failed.");
            QuietControl<TimePicker>("QuietEnd").Time = TimeSpan.FromHours(7);
            for (int i = 0; i < 7; i++) QuietControl<CheckBox>("QuietDay" + i).IsChecked = false;
            InvokeSettingsButton("Save"); await Task.Delay(150);
            if (!QuietControl<InfoBar>("QuietError").IsOpen || _state.Quiet.ScheduleEnabled) throw new Exception("Empty-day validation failed.");
            for (int i = 0; i < 7; i++) QuietControl<CheckBox>("QuietDay" + i).IsChecked = true;
            QuietControl<InfoBar>("QuietError").IsOpen = false;
            QuietControl<CheckBox>("QuietException0").IsChecked = true;
            QuietControl<CheckBox>("QuietBadges").IsChecked = true;
            await CaptureShell("quiet-settings-dark", _activeDialog);
            InvokeSettingsButton("Save"); await pending;
            if (!QuietFor(work).MuteAudio || QuietFor(personal).MuteAudio || !_state.Quiet.HideScheduledBadges)
                throw new Exception("Schedule or exception was not applied.");
            var loaded = _store.Load();
            if (!loaded.Quiet.ScheduleEnabled || !loaded.Quiet.ExcludedAccountIds.Contains(personal.Id))
                throw new Exception("Quiet settings were not persisted.");
            StartFocus(25);
            if (!QuietFor(personal).MuteAudio || personal.Muted) throw new Exception("Focus/exception precedence failed.");
            await Task.Delay(200); await CaptureShell("quiet-overlap-dark");
            var skip = _state.Quiet.Copy(); skip.SkippedOccurrence = CurrentQuiet.Occurrence!.Id;
            CommitQuiet(skip);
            if (!QuietFor(work).MuteAudio) throw new Exception("Skip incorrectly ended focus.");
            now = now.AddMinutes(26); RefreshQuiet();
            if (QuietFor(work).MuteAudio || _state.Quiet.FocusUntil != null) throw new Exception("Focus expiry failed.");
            var resume = _state.Quiet.Copy(); resume.SkippedOccurrence = null; CommitQuiet(resume);
            work.Muted = true; now = now.AddHours(9); RefreshQuiet();
            if (!QuietFor(work).MuteAudio || QuietFor(personal).MuteAudio) throw new Exception("Manual mute restoration failed.");
            _state.Theme = "Light"; ApplyTheme();
            pending = ShowQuietSettings(); await Task.Delay(250); await CaptureShell("quiet-settings-light", _activeDialog);
            InvokeSettingsButton("Cancel"); await pending;
            pending = ShowFocusDialog(); await Task.Delay(250);
            QuietControl<NumberBox>("FocusMinutes").Value = 50;
            QuietControl<CheckBox>("FocusAudio").IsChecked = false;
            QuietControl<CheckBox>("FocusBadges").IsChecked = true;
            await CaptureShell("focus-options-light", _activeDialog);
            InvokeSettingsButton("Start focus"); await pending;
            if (!QuietFor(personal).SuppressNotifications || QuietFor(personal).MuteAudio || !QuietFor(personal).HideBadges)
                throw new Exception("Focus options failed.");
            AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 640));
            await Task.Delay(200);
            pending = ShowQuietSettings(); await Task.Delay(250); await CaptureShell("quiet-settings-narrow", _activeDialog);
            InvokeSettingsButton("Cancel"); await pending;
            await CaptureShell("quiet-toolbar-narrow");
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "quiet-report.json"), JsonSerializer.Serialize(new
            {
                cancelPass = true, equalTimeValidationPass = true, emptyDayValidationPass = true, saveReloadPass = true, exceptionsPass = true,
                overlapPass = true, skipPass = true, expiryPass = true, manualMutePass = true, focusOptionsPass = true,
                browserSessionsOpened = 0, signalSessionsConnected = 0
            }));
        }
        catch (Exception ex) { Diagnostic("quiet-smoke", ex); }
        finally { _activeDialog?.Hide(); Quit(); }
    }
    private T QuietControl<T>(string name) where T : Microsoft.UI.Xaml.FrameworkElement =>
        DialogElements(_activeDialog!).OfType<T>().Single(x => x.Name == name);
}
