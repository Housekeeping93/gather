using Gather.Core;
using Microsoft.UI.Xaml;
using System.Text.Json;
namespace Gather.App;
public sealed partial class MainWindow
{
    private Func<string, string, bool>? _quietTestAlert;
    private async Task RunQuietWebSmokeTest()
    {
        try
        {
            if (_state.Accounts.Count != 0) throw new Exception("Use a fresh offline web profile.");
            var account = new Account { ProviderId = "telegram", Name = "Offline web fixture", StartConnected = false };
            var signal = new Account { ProviderId = "signal", Name = "Offline Signal policy fixture", StartConnected = false };
            _state.Accounts.AddRange([account, signal]);
            _state.Layout.Left = account.Id; _home = false;
            var now = new DateTimeOffset(2026, 11, 2, 23, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 2)));
            _quietClock = () => now;
            _state.Quiet.ScheduleEnabled = true;
            var web = new ServiceView(account, this);
            _views.Add(account.Id, web); ServiceLayer.Children.Add(web.View); BuildRail();
            await web.InitializeQuietDiagnosticAsync();
            await RefreshLayout();
            var popup = await web.CreateQuietDiagnosticPopupAsync();
            bool BothMuted(bool muted) => web.View.CoreWebView2.IsMuted == muted && popup.CoreWebView2.IsMuted == muted;
            if (!BothMuted(true)) throw new Exception("Startup did not mute main/popup before content.");
            var draft = _state.Quiet.Copy(); draft.ExcludedAccountIds.Add(account.Id); CommitQuiet(draft);
            if (!BothMuted(false)) throw new Exception("Exception did not unmute main/popup.");
            StartFocus(25);
            if (!BothMuted(true) || account.Muted) throw new Exception("Focus audio changed preferences or missed popup.");
            draft = _state.Quiet.Copy(); draft.FocusUntil = null; CommitQuiet(draft);
            if (!BothMuted(false)) throw new Exception("Ending focus did not restore exception.");
            QuietSystemChanged(true);
            if (!BothMuted(true)) throw new Exception("Suspend did not mute browser controls.");
            QuietSystemChanged(false);
            if (!BothMuted(false)) throw new Exception("Resume did not restore current policy.");
            int delivered = 0; _quietTestAlert = (_, _) => { delivered++; return true; };
            ShowServiceNotification(account, "Sample", "Sample");
            ShowServiceNotification(signal, "Sample", "Sample");
            if (delivered != 1) throw new Exception("Exception/native Signal notification filtering failed.");
            draft = _state.Quiet.Copy(); draft.ExcludedAccountIds.Clear(); draft.HideScheduledBadges = true; CommitQuiet(draft);
            web.Unread.SetCount(7); UpdateStatus();
            if (!BothMuted(true) || _badges[account.Id].Background.Visibility != Visibility.Collapsed || web.Unread.Count != 7)
                throw new Exception("DND badge/audio handling failed.");
            ShowServiceNotification(account, "Sample", "Sample");
            if (delivered != 1) throw new Exception("DND delivered a covered notification.");
            draft = _state.Quiet.Copy(); draft.SkippedOccurrence = CurrentQuiet.Occurrence!.Id; CommitQuiet(draft);
            if (!BothMuted(false) || _badges[account.Id].Background.Visibility != Visibility.Visible)
                throw new Exception("Skip did not restore browser audio/unread display.");
            account.Muted = true; RefreshQuiet();
            if (!BothMuted(true)) throw new Exception("Manual account mute was lost.");
            now = now.AddHours(9); RefreshQuiet();
            if (!BothMuted(true) || delivered != 1) throw new Exception("Restoration replayed alerts or overrode manual mute.");
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "quiet-web-report.json"), JsonSerializer.Serialize(new
            {
                startupMutePass = true, popupMutePass = true, exceptionPass = true, focusPass = true,
                suspendResumePass = true, notificationFilterPass = true, badgePass = true, skipPass = true,
                manualMutePass = true, providerSessionsOpened = 0, notificationsSentToWindows = 0
            }));
        }
        catch (Exception ex) { Diagnostic("quiet-web-smoke", ex); }
        finally { Quit(); }
    }
}
