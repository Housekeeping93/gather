using Gather.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.System;

namespace Gather.App;

public sealed partial class MainWindow
{
    private async Task RunQuickSwitchSmokeTest()
    {
        bool injectKeyboard = System.Environment.GetCommandLineArgs().Contains("--smoke-quick-switch-keyboard");
        bool realWeb = System.Environment.GetCommandLineArgs().Contains("--smoke-quick-switch-web");
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        T Control<T>(string name) where T : FrameworkElement => DialogElements(_activeDialog!).OfType<T>().Single(x => x.Name == name);
        async Task Search(string text)
        {
            Control<TextBox>("QuickSwitchSearch").Text = text;
            await Task.Delay(150); // TextChanged is dispatched by WinUI.
        }
        async Task WaitFor(Func<bool> ready)
        {
            for (int i = 0; i < 100 && !ready(); i++) await Task.Delay(50);
            Require(ready(), "Quick switch UI timed out.");
        }
        async Task Key(byte key, bool control = false)
        {
            if (!injectKeyboard)
            {
                if (key == 0x4B && control) RequestQuickSwitch();
                else _quickSwitchDiagnosticKey?.Invoke((VirtualKey)key);
                await Task.Delay(200);
                return;
            }
            Require(GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(this), "Diagnostic Gather must have keyboard focus.");
            if (control) { keybd_event(0x11, 0, 0, 0); await Task.Delay(60); }
            keybd_event(key, 0, 0, 0); await Task.Delay(60); keybd_event(key, 0, 2, 0);
            if (control) keybd_event(0x11, 0, 2, 0);
            await Task.Delay(160);
        }
        async Task OpenByKeyboard()
        {
            await Key(0x4B, true);
            await WaitFor(() => _activeDialog?.Title as string == "Switch service");
            await Task.Delay(150);
            Require(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), Control<TextBox>("QuickSwitchSearch")), "Search did not receive focus.");
        }
        try
        {
            Require(_state.Accounts.Count == 0, "Use a fresh quick-switch data directory.");
            Require(_navigationKeys.Available, "Keyboard hook is unavailable.");
            ShowWindow(); QuickSwitchButton.Focus(FocusState.Programmatic); await Task.Delay(300);
            await OpenByKeyboard();
            Require(Control<ListView>("QuickSwitchResults").Items.Count == 0 && !_activeDialog!.IsPrimaryButtonEnabled, "Empty-account state failed.");
            await Key(0x1B); await WaitFor(() => !_quickSwitchOpen);

            var webAccount = new Account { ProviderId = "telegram", Name = "Office Telegram", StartConnected = false };
            var signalAccount = new Account { ProviderId = "signal", Name = "Family", StartConnected = false, Muted = true };
            var other = new Account { ProviderId = "signal", Name = "Personal Signal", StartConnected = false };
            _state.Accounts.AddRange([webAccount, signalAccount, other]);
            var web = realWeb ? new ServiceView(webAccount, this) : null;
            var fixture = realWeb ? null : new QuickSwitchFixtureSession(webAccount, this);
            IServiceSession first = web ?? (IServiceSession)fixture!;
            _views.Add(webAccount.Id, first); ServiceLayer.Children.Add(first.View);
            foreach (var account in new[] { signalAccount, other })
            {
                var session = CreateSession(account, diagnostic: true);
                _views.Add(account.Id, session); ServiceLayer.Children.Add(session.View);
                // Enable only the offline fixture's composer so focus can be tested.
                ((SignalServiceSession)session).EnableQuickSwitchDiagnosticInput();
            }
            _state.Layout.Left = webAccount.Id; _state.Layout.Right = signalAccount.Id;
            _state.Layout.Split = true; _state.Layout.FocusRight = false; _home = false;
            Welcome.Visibility = Visibility.Collapsed; PaneArea.Visibility = Visibility.Visible;
            ApplyColumns(); await WaitFor(() => first.View.IsLoaded);
            if (web != null) await web.InitializeQuietDiagnosticAsync();
            BuildRail(); _state.Theme = "Dark"; ApplyTheme(); await RefreshLayout();
            web?.View.CoreWebView2.NavigateToString("<html><body><label>Offline draft <input id='draft' value='Unsent sample'></label></body></html>");
            await Task.Delay(500);
            int navigations = 0;
            if (web != null) web.View.CoreWebView2.NavigationStarting += (_, _) => navigations++;
            FocusWeb();
            if (web != null) await web.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('draft').focus()");
            await OpenByKeyboard();
            var originalDialog = _activeDialog; await Key(0x4B, true);
            Require(_activeDialog == originalDialog, "Repeated Ctrl+K queued another dialog.");
            await Search("missing account");
            Require(Control<ListView>("QuickSwitchResults").Items.Count == 0 && !_activeDialog!.IsPrimaryButtonEnabled, "No-match state failed.");
            await Key(0x0D); Require(_quickSwitchOpen, "Enter closed an empty result set.");
            await Search("SIGNAL");
            Require(Control<ListView>("QuickSwitchResults").Items.Count == 2, "Provider search failed.");
            await Key(0x28); Require(Control<ListView>("QuickSwitchResults").SelectedIndex == 1, "Down key failed.");
            await Key(0x26); Require(Control<ListView>("QuickSwitchResults").SelectedIndex == 0, "Up key failed.");
            Require(Control<TextBlock>("QuickSwitchSummary").Text.Contains("Family, Signal"), "Selection announcement lacks account/provider.");
            var selectedRow = (ListViewItem)Control<ListView>("QuickSwitchResults").SelectedItem;
            Require(AutomationProperties.GetName(selectedRow) == "Family, Signal", "Accessible row name failed.");
            await Task.Delay(500); await CaptureShell("quick-switch-dark", _activeDialog);
            await Key(0x1B); await WaitFor(() => !_quickSwitchOpen);
            Require(!_state.Layout.FocusRight && navigations == 0, "Escape changed the layout or reloaded web content.");
            if (web != null) Require(await web.View.CoreWebView2.ExecuteScriptAsync("document.activeElement.id") == "\"draft\"", "Escape lost the browser's input focus.");
            else Require(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), fixture!.Input), "Escape lost the previous input focus.");
            await OpenByKeyboard(); await Search("family");
            await Key(0x0D); await WaitFor(() => !_quickSwitchOpen && _state.Layout.FocusRight);
            Require(_state.Layout.Left == webAccount.Id && _state.Layout.Right == signalAccount.Id && _views.Count == 3 && navigations == 0, "Visible-account selection duplicated or reloaded a session.");
            Require(signalAccount.Muted, "Switching changed permanent mute.");
            await WaitFor(() => FocusManager.GetFocusedElement(Root.XamlRoot) is DependencyObject focused && DialogElements(_views[signalAccount.Id].View).Contains(focused));
            await OpenByKeyboard();
            Require(((ListViewItem)Control<ListView>("QuickSwitchResults").Items[0]).Tag as string == signalAccount.Id, "Empty search did not put the recent account first.");
            await Search("personal");
            InvokeSettingsButton("Switch"); await WaitFor(() => !_quickSwitchOpen && _state.Layout.Right == other.Id);
            Require(_state.Layout.Left == webAccount.Id && _state.Layout.FocusRight && _views.Count == 3, "Focused-pane replacement failed.");
            Require(_store.Load().RecentAccountIds.First() == other.Id, "Recent selection was not saved.");
            _state.Theme = "Light"; ApplyTheme(); AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 640));
            await OpenByKeyboard(); await Task.Delay(500); await CaptureShell("quick-switch-light-narrow", _activeDialog);
            InvokeSettingsButton("Cancel"); await WaitFor(() => !_quickSwitchOpen);
            Require(!_quitting && _dialogs.CurrentCount == 1, "Closing the switcher did not release the dialog guard.");
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "quick-switch-report.json"), JsonSerializer.Serialize(new
            {
                emptyAccountsPass = true, nativeSignalSwitcherPass = true,
                repeatShortcutPass = true, searchPass = true, arrowKeysPass = true, enterPass = true,
                escapeFocusPass = true, visibleAccountPass = true, focusedPanePass = true,
                recentPersistencePass = true, accessibleNamesAndLiveRegionPass = true,
                permanentMutePass = true, browserNavigationsDuringSwitching = realWeb ? (int?)navigations : null,
                providerSessionsOpened = 0, signalSessionsConnected = 0,
                screenReaderSpeechManuallyVerified = false,
                keyboardInjectionExercised = injectKeyboard, realWebView2Exercised = realWeb
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Diagnostic("quick-switch-smoke", ex);
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "quick-switch-failure.txt"), ex.ToString());
        }
        finally { _activeDialog?.Hide(); Quit(); }
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    // A native input fixture lets the dialog checks run where browser controllers
    // cannot be created. Real WebView2 coverage is a separate explicit mode.
    private sealed class QuickSwitchFixtureSession : IServiceSession
    {
        public Account Account { get; }
        public UserControl View { get; } = new();
        FrameworkElement IServiceSession.View => View;
        public TextBox Input { get; } = new() { Text = "Unsent sample", AcceptsReturn = true };
        public string Status => "Offline quick-switch fixture";
        public bool Ready => true;
        public UnreadState Unread { get; } = new();
        public QuickSwitchFixtureSession(Account account, MainWindow host)
        {
            Account = account; View.Content = Input;
            View.GotFocus += (_, _) => host.FocusAccount(account.Id);
        }
        public Task InitializeAsync() => Task.CompletedTask;
        public Task ClearDataAsync() => Task.CompletedTask;
        public Task SetContentSize(double zoom) => Task.CompletedTask;
        public void ApplyTheme() { }
        public void SetAudioMuted(bool muted) { }
        public void Acknowledge() => Unread.Acknowledge();
        public void FocusContent() => Input.Focus(FocusState.Programmatic);
        public void Close() { }
    }
}
