using Gather.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Text.Json;

namespace Gather.App;

public sealed partial class MainWindow
{
    private async Task RunPrivacySmokeTest()
    {
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        async Task Settle() { Root.UpdateLayout(); await Task.Delay(200); }
        bool webMode = System.Environment.GetCommandLineArgs().Contains("--privacy-web");
        Window? popup = null;
        try
        {
            Require(_state.Accounts.Count == 0, "Use an empty privacy diagnostic profile.");
            foreach (string name in new[] { "Private personal account", "Private work account" })
            {
                var account = new Account { ProviderId = "telegram", Name = name, StartConnected = false };
                _state.Accounts.Add(account);
                IServiceSession session = webMode ? new ServiceView(account, this) : new QuickSwitchFixtureSession(account, this);
                _views.Add(account.Id, session); ServiceLayer.Children.Add(session.View);
            }
            var ids = _state.Accounts.Select(a => a.Id).ToArray();
            _state.Layout = new() { Left = ids[0], Right = ids[1], Split = true, FocusRight = true, Ratio = .35 };
            _home = false; _state.HidePreviews = false;
            BuildRail(); await RefreshLayout(); await Settle();
            if (webMode)
            {
                foreach (var web in _views.Values.Cast<ServiceView>())
                {
                    await web.InitializeQuietDiagnosticAsync();
                    await Task.Delay(250);
                    await web.View.CoreWebView2.ExecuteScriptAsync("document.body.innerHTML='<textarea id=\"draft\">Unsent private draft</textarea>'; document.getElementById('draft').focus()");
                }
                await ((ServiceView)_views[ids[0]]).CreateQuietDiagnosticPopupAsync();
            }
            else
            {
                popup = new Window { Title = "Private popup", Content = new TextBox { Text = "Private popup draft" } };
                popup.Activate(); RegisterPrivacyPopup(popup);
            }
            ShowWindow(); FocusWeb(); await Settle();
            var windowCases = new List<object>();
            foreach (string mode in new[] { "CustomSmall", "CustomLarge", "Maximized" })
            {
                var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;
                presenter.Restore();
                AppWindow.Resize(mode == "CustomSmall" ? new Windows.Graphics.SizeInt32(1060, 720) : new Windows.Graphics.SizeInt32(1360, 900));
                if (mode == "Maximized") presenter.Maximize();
                await Settle();
                var size = AppWindow.Size;
                var position = AppWindow.Position;
                var state = presenter.State;
                int savedWidth = _state.WindowWidth, savedHeight = _state.WindowHeight;
                void CheckWindow()
                {
                    Require(AppWindow.Size.Width == size.Width && AppWindow.Size.Height == size.Height
                        && AppWindow.Position.X == position.X && AppWindow.Position.Y == position.Y
                        && presenter.State == state, "Curtain changed window bounds/state in " + mode);
                    Require(_state.WindowWidth == savedWidth && _state.WindowHeight == savedHeight,
                        "Curtain changed saved window dimensions in " + mode);
                }
                RequestPrivacy(); await Settle(); CheckWindow();
                // A covered popup trying to activate also routes through ShowWindow.
                foreach (var child in _privacyPopups.ToArray()) child.Activate();
                await Settle(); CheckWindow();
                Require(_privacyPopups.All(p => !p.AppWindow.IsVisible), "Covered popup became visible.");
                RequestPrivacy(); await Settle(); CheckWindow();
                windowCases.Add(new { mode, width = size.Width, height = size.Height, x = position.X, y = position.Y, state = state.ToString(), passed = true });
            }
            ((Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter).Restore();
            await Settle();
            int alerts = 0; _quietTestAlert = (_, _) => { alerts++; return true; };
            ShowServiceNotification(_state.Accounts[0], "Private sender", "Private text");
            Require(alerts == 1, "Uncovered notification routing failed.");
            foreach (string theme in new[] { "Dark", "Light" })
            foreach (bool focus in new[] { false, true })
            {
                _state.Theme = theme; _state.FocusView = focus; ApplyTheme(); ApplyShellAppearance(); await Settle();
                var layout = JsonSerializer.Serialize(_state.Layout);
                int count = _views.Count;
                RequestPrivacy(); await Settle();
                Require(PrivateShell.Visibility == Visibility.Collapsed && PrivacyCover.Visibility == Visibility.Visible,
                    "Sensitive shell is exposed.");
                Require(_views.Values.All(v => v.View.Visibility == Visibility.Collapsed && v.View.Parent == ServiceLayer), "Sessions must be hidden and attached.");
                Require(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), RestorePrivacyButton), "Focus did not move to the restore control.");
                Require(!_views.Values.Any(v => IsAccountActive(v.Account.Id)), "Covered account is treated as active.");
                Require(_privacyPopups.Count > 0 && _privacyPopups.All(p => !p.AppWindow.IsVisible), "Registered popup is exposed.");
                ShowServiceNotification(_state.Accounts[1], "Private sender", "Private text");
                Require(alerts == 1, "Covered notification leaked.");
                RequestSplitView(); NavigateServices(1); RequestQuickSwitch(); RequestFocusView(); FocusWeb();
                await Settle();
                Require(_activeDialog == null && JsonSerializer.Serialize(_state.Layout) == layout && _state.FocusView == focus,
                    "A covered shortcut modified the layout or opened a dialog.");
                Require(await ShowDialog(Dialog("Private dialog", new TextBox(), "Save", "Cancel")) == ContentDialogResult.None,
                    "A new dialog opened behind the curtain.");
                Require(_store.Load().PrivacyCovered, "Privacy did not persist.");
                await CaptureShell("privacy-" + theme.ToLowerInvariant() + (focus ? "-focus" : ""));
                RequestPrivacy(); await Settle();
                Require(JsonSerializer.Serialize(_state.Layout) == layout && _views.Count == count, "Restore changed the layout or sessions.");
                Require(_privacyPopups.All(p => p.AppWindow.IsVisible), "Registered popups did not return.");
                Require(!_store.Load().PrivacyCovered, "Restore did not persist.");
                if (webMode)
                    foreach (var web in _views.Values.Cast<ServiceView>())
                        Require((await web.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('draft').value")).Contains("Unsent private draft"), "Browser draft lost.");
                else Require(_views.Values.Cast<QuickSwitchFixtureSession>().All(v => v.Input.Text == "Unsent sample"), "Draft lost.");
            }
            var pending = ShowSettings(); await Task.Delay(350);
            Require(_activeDialog != null, "Settings did not open.");
            DialogElements(_activeDialog!).OfType<ComboBox>().Single(c => c.Name == "PrivacyShortcut").SelectedItem = "Ctrl+Shift+L";
            RequestPrivacy(); await pending; await Settle();
            Require(_privacyCovered && _activeDialog == null && _state.PrivacyShortcut == "Ctrl+Shift+P", "Curtain did not cancel settings safely.");
            Require(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), RestorePrivacyButton), "Dialog stole focus after cover.");
            RequestPrivacy(); await Settle();
            pending = ShowSettings(); await Task.Delay(350);
            DialogElements(_activeDialog!).OfType<ComboBox>().Single(c => c.Name == "PrivacyShortcut").SelectedItem = "Ctrl+Shift+L";
            InvokeSettingsButton("Save"); await pending;
            Require(_store.Load().PrivacyShortcut == "Ctrl+Shift+L", "Shortcut selection did not save.");
            string focused = FocusedId!;
            RequestSplitView(); await Settle();
            Require(!_state.Layout.Split && _state.Layout.Left == focused && _state.Layout.Ratio == .35, "Split collapse lost focused account or ratio.");
            RequestSplitView(); await Settle();
            Require(_state.Layout.Split && _views.Count == 2 && ServiceLayer.Children.Count == 2, "Split restore recreated sessions.");
            // Quick switch can be open when the user needs privacy urgently.
            RequestQuickSwitch(); await Task.Delay(350); Require(_activeDialog != null, "Quick switch did not open.");
            RequestPrivacy(); await Task.Delay(350); Require(_activeDialog == null && !_quickSwitchOpen, "Quick switch did not dismiss.");
            RequestPrivacy();
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "privacy-report.json"), JsonSerializer.Serialize(new
            {
                shellAndFocusPass = true, popupHideRestorePass = true, dialogCancellationPass = true,
                notificationSuppressionPass = true, sessionAndDraftRetentionPass = true, settingsPersistencePass = true,
                splitTogglePass = true, windowGeometryPass = true, windowCases, webMode, threadShortcutHookAvailable = _navigationKeys.Available,
                physicalShortcutTested = false, narratorTested = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(_store.DirectoryPath, "privacy-failure.txt"), ex.ToString()); }
        finally { _activeDialog?.Hide(); popup?.Close(); Quit(); }
    }
}
