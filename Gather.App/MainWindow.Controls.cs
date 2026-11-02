using System.Diagnostics;
using Gather.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Media.Imaging;
namespace Gather.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, BitmapImage> _logos = [];
    private Image ServiceLogo(Provider provider, double size)
    {
        if (!_logos.TryGetValue(provider.Id, out var source))
        {
            source = new BitmapImage { DecodePixelWidth = 96, UriSource = new Uri($"ms-appx:///Assets/Services/{provider.Id}.png") };
            _logos.Add(provider.Id, source);
        }
        return new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }
    private static SolidColorBrush Brush(string hex) => new(Windows.UI.Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16)));
    private static TextBlock Text(string text, double size = 14, bool bold = false) => new() { Text = text, FontSize = size, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    private void BuildCards()
    {
        ServiceCards.Children.Clear();
        for (int i = 0; i < Providers.All.Length; i++)
        {
            var p = Providers.All[i];
            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(new Border { Width = 56, Height = 56, Padding = new(14), HorizontalAlignment = HorizontalAlignment.Left, Child = ServiceLogo(p, 28) });
            stack.Children.Add(Text(p.Name, 17, true));
            var desc = Text(p.Description, 12);
            desc.Opacity = .65;
            stack.Children.Add(desc);
            var button = new Button { Content = stack, Padding = new(16), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 158, CornerRadius = new(12) };
            button.Click += async (_, _) => await AddAccount(p);
            Grid.SetColumn(button, i % 3);
            Grid.SetRow(button, i / 3);
            ServiceCards.Children.Add(button);
        }
        var card = new Border { Padding = new(20), CornerRadius = new(12), Background = Brush(IsDark ? "#203C35" : "#E7F2EF") };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = "◫", FontSize = 30, Foreground = Brush(IsDark ? "#8BDFC2" : "#326956") });
        content.Children.Add(new TextBlock { Text = "Two services.\nOne view.", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(IsDark ? "#E0F5EC" : "#254F42") });
        content.Children.Add(new TextBlock { Text = "Connect two accounts to try split view.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush(IsDark ? "#B8D9CC" : "#426B5B") });
        card.Child = content;
        Grid.SetRow(card, 2);
        Grid.SetColumnSpan(card, 3);
        ServiceCards.Children.Add(card);
    }
    private void BuildRail()
    {
        PreviousButton.IsEnabled = NextButton.IsEnabled = _state.Accounts.Count > 1;
        ServiceRail.Children.Clear();
        _badges.Clear();
        foreach (var a in _state.Accounts)
        {
            var p = Providers.Find(a.ProviderId)!;
            var grid = new Grid { Width = 52, Height = 52 };
            grid.Children.Add(ServiceLogo(p, 28));
            var badge = new TextBlock { FontSize = 11, Foreground = Brush("#FFFFFF"), FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border { Child = badge, MinWidth = 21, Height = 21, Padding = new(4, 0, 4, 0), CornerRadius = new(11), Background = Brush("#B4233D"), BorderBrush = Brush("#FFFFFF"), BorderThickness = new(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 1, 1, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            grid.Children.Add(pill);
            var button = new Button { Content = grid, Width = 52, Height = 52, Padding = new(0), CornerRadius = new(10), BorderThickness = new(0), HorizontalAlignment = HorizontalAlignment.Center };
            _badges[a.Id] = (pill, badge, button);
            ToolTipService.SetToolTip(button, a.Name);
            button.Click += (_, _) => SelectAccount(a.Id);
            button.CanDrag = true;
            button.AllowDrop = true;
            button.DragStarting += (_, e) => { e.Data.SetText("gather-account:" + a.Id); e.Data.RequestedOperation = DataPackageOperation.Move; };
            button.DragOver += (_, e) => { e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.Text) ? DataPackageOperation.Move : DataPackageOperation.None; e.Handled = true; };
            button.Drop += async (_, e) =>
            {
                if (!e.DataView.Contains(StandardDataFormats.Text))
                    return;
                var position = e.GetPosition(button);
                var deferral = e.GetDeferral();
                try
                {
                    var text = await e.DataView.GetTextAsync();
                    if (!text.StartsWith("gather-account:", StringComparison.Ordinal))
                        return;
                    string source = text[15..];
                    int from = _state.Accounts.FindIndex(x => x.Id == source), to = _state.Accounts.FindIndex(x => x.Id == a.Id);
                    if (from < 0 || to < 0 || from == to)
                        return;
                    if (position.Y > button.ActualHeight / 2)
                        to++;
                    if (from < to)
                        to--;
                    ReorderAccount(source, to);
                    e.Handled = true;
                }
                catch (Exception ex) { Diagnostic("reorder", ex); }
                finally { deferral.Complete(); }
            };
            var menu = new MenuFlyout();
            MenuItem(menu, "Open beside this", () => OpenBeside(a.Id));
            MenuItem(menu, "Account settings", async () => await AccountSettings(a));
            MenuItem(menu, "Move up", () => ReorderAccount(a.Id, _state.Accounts.IndexOf(a) - 1));
            MenuItem(menu, "Move down", () => ReorderAccount(a.Id, _state.Accounts.IndexOf(a) + 1));
            button.ContextFlyout = menu;
            if (p.Kind == ProviderKind.Signal)
            {
                // The official glyph keeps its full clear space; its badge sits below it.
                grid.Children.Remove(pill);
                pill.HorizontalAlignment = HorizontalAlignment.Center;
                pill.Margin = new(0);
                var signalTile = new StackPanel { Spacing = 2 };
                signalTile.Children.Add(button);
                signalTile.Children.Add(pill);
                ServiceRail.Children.Add(signalTile);
            }
            else ServiceRail.Children.Add(button);
        }
        UpdateStatus();
    }
    private void ReorderAccount(string id, int destination)
    {
        if (!LayoutActions.MoveAccount(_state, id, destination))
            return;
        Save();
        BuildRail();
        BuildPaneHeader(LeftHeader, false);
        BuildPaneHeader(RightHeader, true);
    }
    private static void MenuItem(MenuFlyout menu, string label, Action action)
    {
        var item = new MenuFlyoutItem { Text = label };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
    private void BuildPaneHeader(Grid header, bool right)
    {
        header.Children.Clear();
        header.ColumnDefinitions.Clear();
        header.ColumnDefinitions.Add(new()
        {
            Width = new(1, GridUnitType.Star)
        });
        header.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var account = right ? RightAccount : LeftAccount;
        var selector = new DropDownButton
        {
            Content = new TextBlock { Text = account?.Name ?? "Choose service", TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
            HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 240, MinWidth = 0, Height = 30,
            Padding = new(8, 0, 8, 0), CornerRadius = new(6), BorderThickness = new(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
        ToolTipService.SetToolTip(selector, account?.Name ?? "Choose service");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(selector, "Choose service: " + (account?.Name ?? "No service"));
        var options = new MenuFlyout();
        foreach (var a in _state.Accounts)
            MenuItem(options, a.Name, () => SelectAccount(a.Id, right));
        selector.Flyout = options;
        header.Children.Add(selector);
        var more = new Button { Content = "\uE712", FontFamily = new FontFamily("Segoe Fluent Icons"), Style = (Style)Root.Resources["ShellButtonStyle"], Width = 30, MinWidth = 30, Height = 30, Padding = new(0) };
        ToolTipService.SetToolTip(more, "Service options");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(more, "Service options");
        var menu = new MenuFlyout();
        if (account != null)
        {
            MenuItem(menu, "Smaller content", async () => await ResizeContent(account, -.1));
            MenuItem(menu, "Larger content", async () => await ResizeContent(account, .1));
            MenuItem(menu, "Reset content size", async () => { var view = await EnsureView(account); await view.SetContentSize(1); Save(); });
            MenuItem(menu, account.ProviderId == "signal" ? "Reconnect" : "Reload", async () => await ReloadAccount(account));
            if (account.ProviderId == "slack")
                MenuItem(menu, "Restart Slack sign-in", async () => { var view = await EnsureView(account); if (view is ServiceView web) web.RestartSignIn(); });
            if (Providers.Find(account.ProviderId)!.Kind == ProviderKind.Web)
                MenuItem(menu, "Open website in browser", () => OpenExternal(NavigationPolicy.StartUrl(account)));
            MenuItem(menu, "Account settings", async () => await AccountSettings(account));
        }
        if (_state.Layout.Split)
            MenuItem(menu, "Keep only this pane", () => { _state.Layout.FocusRight = right; LayoutActions.Collapse(_state.Layout); _ = RefreshLayout(); });
        more.Flyout = menu;
        Grid.SetColumn(more, 1);
        header.Children.Add(more);
    }
    private async Task AddAccount(Provider p)
    {
        if (p.Kind == ProviderKind.Signal && _state.Accounts.Any(a => a.ProviderId == p.Id))
        {
            SelectAccount(_state.Accounts.First(a => a.ProviderId == p.Id).Id);
            return;
        }
        var name = new TextBox { Header = "Account name", Text = p.Name, MaxLength = 60 };
        var url = new TextBox { Header = "Workspace URL (optional)", PlaceholderText = "https://your-team.slack.com" };
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(Text(p.Description, 14));
        stack.Children.Add(name);
        if (p.Id == "slack")
        {
            stack.Children.Add(url);
            stack.Children.Add(Text("Google and Apple sign-in may open your browser and might not return a session here. Use Slack’s emailed code if your workspace permits it.", 12));
        }
        var dialog = Dialog("Add " + p.Name, stack, "Continue", "Cancel");
        dialog.PrimaryButtonClick += (_, e) => { if (p.Id == "slack" && !string.IsNullOrWhiteSpace(url.Text) && NavigationPolicy.Decide(p, url.Text.Trim()) != NavigationDecision.Internal) { url.Header = "Enter a valid HTTPS slack.com workspace URL"; e.Cancel = true; } };
        if (await ShowDialog(dialog) != ContentDialogResult.Primary)
            return;
        var a = new Account { ProviderId = p.Id, Name = string.IsNullOrWhiteSpace(name.Text) ? p.Name : name.Text.Trim(), StartUrl = string.IsNullOrWhiteSpace(url.Text) ? null : url.Text.Trim() };
        _state.Accounts.Add(a);
        LayoutActions.Select(_state.Layout, a.Id);
        _home = false;
        Save();
        BuildRail();
        await RefreshLayout();
    }
    private async Task ResizeContent(Account account, double delta)
    {
        var view = await EnsureView(account);
        await view.SetContentSize(Math.Round(account.Zoom + delta, 2));
        Save();
    }
    private async Task AccountSettings(Account a)
    {
        var name = new TextBox { Header = "Account name", Text = a.Name, MaxLength = 60 };
        var mute = new ToggleSwitch { Header = "Mute this account", IsOn = a.Muted };
        var startup = new ToggleSwitch { Header = "Keep connected on startup", IsOn = a.StartConnected };
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(name);
        content.Children.Add(mute);
        content.Children.Add(startup);
        content.Children.Add(Text(a.ProviderId == "signal" ? "Removing Signal clears its Gather messages and connection on this computer. Also remove Gather in Signal on your phone under Linked devices." : "Removing this account clears its saved website data on this computer. You can also revoke the linked device in the service’s own settings.", 12));
        var dialog = Dialog("Account settings", content, "Save", "Cancel");
        dialog.SecondaryButtonText = "Remove account";
        var result = await ShowDialog(dialog);
        if (result == ContentDialogResult.Primary)
        {
            a.Name = string.IsNullOrWhiteSpace(name.Text) ? Providers.Find(a.ProviderId)!.Name : name.Text.Trim();
            a.Muted = mute.IsOn;
            a.StartConnected = startup.IsOn;
            if (_views.TryGetValue(a.Id, out var v))
                v.SetAudioMuted(QuietFor(a).MuteAudio);
            Save();
            BuildRail();
            await RefreshLayout();
        }
        else if (result == ContentDialogResult.Secondary)
        {
            var confirm = Dialog("Remove " + a.Name + "?", Text(a.ProviderId == "signal" ? "Remove Signal messages and connection data from Gather? Also revoke Gather in your phone’s Linked devices. Your other services are kept." : "This signs this account out of Gather and removes its saved website data. Other accounts are kept."), "Remove", "Keep account");
            if (await ShowDialog(confirm) == ContentDialogResult.Primary)
                await RemoveAccount(a);
        }
    }
    private async Task RemoveAccount(Account a)
    {
        try
        {
            var view = await EnsureView(a);
            if (view is ServiceView && !view.Ready)
            {
                ShowNotice("Account kept", "Its website data could not be cleared. Restart Gather and try again.", InfoBarSeverity.Warning);
                return;
            }
            await view.ClearDataAsync();
            view.Close();
            if (view.View.Parent is Panel parent)
                parent.Children.Remove(view.View);
            _views.Remove(a.Id);
            _state.Accounts.Remove(a);
            LayoutActions.Normalize(_state);
            _home = _state.Accounts.Count == 0;
            Save();
            BuildRail();
            await RefreshLayout();
        }
        catch (Exception e) { Diagnostic("account-removal", e); ShowNotice("Could not remove account", "Your other accounts have been kept. Restart Gather and try again.", InfoBarSeverity.Error); }
    }
    private async Task ReloadAccount(Account account)
    {
        try
        {
            if (_views.TryGetValue(account.Id, out var old))
            {
                if (old is ServiceView web && web.Ready)
                {
                    web.Reload();
                    return;
                }
                old.Close();
                ServiceLayer.Children.Remove(old.View);
                _views.Remove(account.Id);
            }
            if (Providers.Find(account.ProviderId)!.Kind == ProviderKind.Web) _environment = null;
            await EnsureView(account);
            await RefreshLayout();
        }
        catch (Exception e) { Diagnostic("reload", e); ShowNotice("Reload failed", "Close Gather from its tray menu and reopen it to retry.", InfoBarSeverity.Error); }
    }
    private ContentDialog Dialog(string title, object content, string primary, string close) => new() { XamlRoot = Root.XamlRoot, Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
    internal Task<ContentDialogResult> ShowSignalDialog(ContentDialog dialog) => ShowDialog(dialog);
    private async Task<ContentDialogResult> ShowDialog(ContentDialog dialog)
    {
        await _dialogs.WaitAsync();
        try
        {
            _activeDialog = dialog;
            return await dialog.ShowAsync();
        }
        finally { _activeDialog = null; _dialogs.Release(); }
    }
    internal async Task<bool> RequestPermissionAsync(Account a, string permission) => await ShowDialog(Dialog(a.Name + " requests permission", Text("Allow " + permission.ToLowerInvariant() + " for this account’s website?"), "Allow", "Block")) == ContentDialogResult.Primary;
    public void ShowNotice(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        Notice.Title = title;
        Notice.Message = message;
        Notice.Severity = severity;
        Notice.ActionButton = null;
        Notice.IsOpen = true;
    }
    internal void OfferExternalLogin(ServiceView service)
    {
        ShowNotice("Choose a sign-in route", service.Provider.Id == "slack"
            ? "To connect Slack here, restart sign-in and choose email code if your workspace allows it. Google/Apple browser sign-in stays in your browser."
            : "This login needs your browser. Start a fresh sign-in there; its session will not transfer into Gather.", InfoBarSeverity.Warning);
        var action = new Button { Content = service.Provider.Id == "slack" ? "Restart in Gather" : "Sign in in browser" };
        action.Click += (_, _) =>
        {
            if (service.Provider.Id == "slack")
            {
                service.RestartSignIn();
                Notice.IsOpen = false;
            }
            else
                OpenExternal(NavigationPolicy.FreshSignInUrl(service.Account));
        };
        Notice.ActionButton = action;
    }
    public void OfferExternalPage(string url)
    {
        ShowNotice("Continue on another website", "This service is redirecting outside its usual website. Open it in your browser to continue.");
        var b = new Button { Content = "Open in browser" };
        b.Click += (_, _) => OpenExternal(url);
        Notice.ActionButton = b;
    }
    public void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https" || !string.IsNullOrEmpty(u.UserInfo))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception e) { Diagnostic("external-link", e); ShowNotice("Could not open browser", "Open the service in your usual browser manually."); }
    }
    private async void Settings_Click(object s, RoutedEventArgs e) => await ShowSettings();
    private async Task ShowSettings(StartupRegistration? startupOverride = null)
    {
        bool openQuietSettings = false;
        var startupRegistration = startupOverride ?? new StartupRegistration();
        bool startupAllowed = startupOverride != null || (!_smoke && !System.Environment.GetCommandLineArgs().Contains("--startup-check"));
        var startup = new ToggleSwitch { Header = "Start Gather when Windows starts", IsEnabled = startupAllowed, Name = "StartWithWindowsToggle" };
        var startupError = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        if (startupAllowed)
        {
            try { startup.IsOn = !string.IsNullOrWhiteSpace(startupRegistration.ReadCommand()); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                startup.IsEnabled = false;
                startupError.Message = "Windows couldn't read the startup setting. Reopen Gather and try again.";
                startupError.IsOpen = true;
                Diagnostic("startup-setting-read", error);
            }
        }
        var theme = new ComboBox { Header = "Appearance", ItemsSource = new[] { "Light", "Dark", "System" }, SelectedItem = _state.Theme, HorizontalAlignment = HorizontalAlignment.Stretch };
        var tray = new ToggleSwitch { Header = "Keep running when the window closes", IsOn = _state.CloseToTray };
        var previews = new ToggleSwitch { Header = "Hide message previews in notifications", IsOn = _state.HidePreviews };
        var badgeMode = new ComboBox { Header = "Taskbar badge", ItemsSource = new[] { "Number", "Dot", "Off" }, SelectedItem = _state.TaskbarBadge, HorizontalAlignment = HorizontalAlignment.Stretch };
        var content = new StackPanel { Spacing = 16 };
        var quietSettings = new Button { Content = "Quiet hours & exceptions…" };
        quietSettings.Click += (_, _) => { openQuietSettings = true; _activeDialog?.Hide(); };
        content.Children.Add(quietSettings);
        content.Children.Add(theme);
        content.Children.Add(Text("Gather 0.10.0 · Signal development preview", 12, true));
        content.Children.Add(Text("Dark mode follows through to websites that support the system theme. A service’s own appearance setting may override it.", 12));
        content.Children.Add(startup);
        content.Children.Add(Text("Opens Gather after you sign in to Windows. Save to apply. Windows Settings → Apps → Startup can also pause it.", 12));
        content.Children.Add(startupError);
        content.Children.Add(badgeMode);
        content.Children.Add(Text("Numbers combine reported unread counts from unmuted accounts. A dot means new activity without a count; + means at least that many. Windows must allow taskbar badges.", 12));
        content.Children.Add(tray);
        content.Children.Add(previews);
        var test = new Button { Content = "Test notification" };
        var testStatus = Text("", 12);
        test.Click += (_, _) => testStatus.Text = TestNotification()
            ? "Test sent to Windows. If no alert appears, check Windows notification settings and Do not disturb."
            : "Windows could not accept the test notification.";
        content.Children.Add(test);
        content.Children.Add(testStatus);
        content.Children.Add(Text("Gather is independent of the services you connect. Service names and logos belong to their respective owners. Gather adds no analytics; connected websites apply their own privacy policies.", 12));
        content.Children.Add(Text("Notifications need the account to remain loaded. This preview keeps loaded accounts active. Web services use their own sign-in. Signal links from your phone.", 12));
        content.Children.Add(Text(_native.TrayAvailable
            ? "Using basic Windows tray alerts. Clicking an alert opens Gather; it does not jump to a conversation. Message preview privacy still applies."
            : "Notifications are unavailable in this session. Check your services for new messages.", 12));
        var dialog = Dialog("Make it yours", new ScrollViewer { Content = content, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, "Save", "Cancel");
        dialog.SecondaryButtonText = "Quit Gather";
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!startup.IsEnabled) return;
            try
            {
                string defaultData = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Gather");
                string? customData = string.Equals(Path.TrimEndingDirectorySeparator(_store.DirectoryPath), defaultData, StringComparison.OrdinalIgnoreCase) ? null : _store.DirectoryPath;
                startupRegistration.SetEnabled(startup.IsOn, System.Environment.ProcessPath ?? "", customData);
                startupError.IsOpen = false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or ArgumentException)
            {
                args.Cancel = true;
                startupError.Message = error is InvalidOperationException ? error.Message : "Windows couldn't save the startup setting. Try again.";
                startupError.IsOpen = true;
                Diagnostic("startup-setting-save", error);
            }
        };
        var result = await ShowDialog(dialog);
        if (openQuietSettings) { await ShowQuietSettings(); return; }
        if (result == ContentDialogResult.Secondary)
        {
            Quit();
            return;
        }
        if (result != ContentDialogResult.Primary)
            return;
        _state.Theme = (string)theme.SelectedItem;
        _state.TaskbarBadge = (string)badgeMode.SelectedItem;
        _state.CloseToTray = tray.IsOn;
        _state.HidePreviews = previews.IsOn;
        ApplyTheme();
        UpdateStatus();
        Save();
    }
}


