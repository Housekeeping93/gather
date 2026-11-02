using System.Diagnostics;
using System.Text.Json;
using Gather.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;


using Windows.System;
namespace Gather.App;

public sealed partial class MainWindow : Window
{
    private readonly SettingsStore _store;
    private readonly Settings _state;
    private readonly Dictionary<string, IServiceSession> _views = [];
    private readonly Dictionary<string, (Border Background, TextBlock Label, Button Tile)> _badges = [];

    private readonly SemaphoreSlim _dialogs = new(1);
    private ContentDialog? _activeDialog;
    private readonly NativeWindow _native;
    private readonly ServiceNavigationKeys _navigationKeys;
    private Task<CoreWebView2Environment>? _environment;
    private bool _home = true, _quitting, _refreshing, _refreshAgain, _dragging;
    private double _dragStart, _ratioStart;
    private readonly bool _smoke;
    public bool DiagnosticMode
    {
        get;
    }
    private Account? LeftAccount => _state.Accounts.FirstOrDefault(x => x.Id == _state.Layout.Left);
    private Account? RightAccount => _state.Accounts.FirstOrDefault(x => x.Id == _state.Layout.Right);
    private string? FocusedId => _state.Layout.FocusRight ? _state.Layout.Right : _state.Layout.Left;
    internal bool IsDark => Root.ActualTheme == ElementTheme.Dark;
    internal string ThemePreference => _state.Theme;

    public MainWindow()
    {
        StartupLog.Write("window-xaml-initialize");
        InitializeComponent();
        StartupLog.Write("window-xaml-initialized");
        var args = System.Environment.GetCommandLineArgs();
        var dataAt = Array.IndexOf(args, "--data-dir");
        var data = dataAt >= 0 && dataAt + 1 < args.Length ? args[dataAt + 1] : Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Gather");
        _smoke = args.Contains("--smoke-test");
        DiagnosticMode = args.Contains("--diagnostics");
        _store = new(data, "settings-signal-preview.json");
        _state = _store.Load();
        if (_smoke && args.Contains("--seed-services") && _state.Accounts.Count == 0)
        {
            foreach (var p in Providers.All.Take(3))
                _state.Accounts.Add(new()
                {
                    ProviderId = p.Id,
                    Name = p.Name
                });
            LayoutActions.EnableSplit(_state.Layout, _state.Accounts.Select(a => a.Id));
            SaveInitialState();
        }
        Title = "Gather";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min(_state.WindowWidth, area.Width), Math.Min(_state.WindowHeight, area.Height)));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min(960, area.Width);
            presenter.PreferredMinimumHeight = Math.Min(640, area.Height);
        }
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidSizeChange && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p && p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Restored)
            {
                _state.WindowWidth = AppWindow.Size.Width;
                _state.WindowHeight = AppWindow.Size.Height;
            }
        };
        Root.SizeChanged += (_, _) => UpdateShellOverflow();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Gather.ico"));
        _native = new(WinRT.Interop.WindowNative.GetWindowHandle(this), ShowWindow, Quit, QuietSystemChanged, async () => { ShowWindow(); await ShowQuietSettings(); });
        _navigationKeys = new(OwnsShortcutWindow, direction =>
        {
            if (_dialogs.CurrentCount == 0 || _quitting)
                return false;
            return DispatcherQueue.TryEnqueue(() => NavigateServices(direction));
        }, RequestQuickSwitch, RequestFocusView, RequestSplitView, RequestPrivacy, () => _state.PrivacyShortcut);
        Closed += (_, _) => _navigationKeys.Dispose();
        Root.ActualThemeChanged += (_, _) => RefreshTheme();
        Activated += (_, e) => { _windowActive = e.WindowActivationState != WindowActivationState.Deactivated; if (_windowActive) { RefreshQuiet(); AcknowledgeFocused(); } };
        PaneArea.SizeChanged += (_, _) => PositionViews();
        LeftPane.SizeChanged += (_, _) => PositionViews();
        ApplyTheme();
        BuildCards();
        BuildRail();
        InitializeQuiet();
        InitializeShellAppearance();
        InitializePrivacy();
        AppWindow.Closing += (_, e) => { if (!_quitting && _state.CloseToTray && _native.TrayAvailable) { e.Cancel = true; Save(); _native.Hide(); } };
        Closed += (_, _) => { _quitting = true; Save(); foreach (var v in _views.Values.ToArray()) v.Close(); _native.Dispose(); };
        AddShortcut(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, ToggleSplit);
        AddShortcut(VirtualKey.F, VirtualKeyModifiers.Menu, ToggleSplit);
        AddShortcut(VirtualKey.K, VirtualKeyModifiers.Control, () => RequestQuickSwitch());
        AddShortcut(VirtualKey.F, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, ToggleFocusView);
        AddShortcut(VirtualKey.Tab, VirtualKeyModifiers.Control, () => NavigateServices(1));
        AddShortcut(VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => NavigateServices(-1));
        for (int i = 0; i < 9; i++)
        {
            int index = i;
            AddShortcut((VirtualKey)((int)VirtualKey.Number1 + i), VirtualKeyModifiers.Control, () => { if (index < _state.Accounts.Count) SelectAccount(_state.Accounts[index].Id); });
        }
        DividerHandle.PointerPressed += (_, e) => { _dragging = true; _dragStart = e.GetCurrentPoint(PaneArea).Position.X; _ratioStart = _state.Layout.Ratio; DividerHandle.CapturePointer(e.Pointer); e.Handled = true; };
        DividerHandle.PointerMoved += (_, e) => { if (_dragging) { _state.Layout.Ratio = Math.Clamp(_ratioStart + (e.GetCurrentPoint(PaneArea).Position.X - _dragStart) / Math.Max(1, PaneArea.ActualWidth), .3, .7); ApplyColumns(); } };
        DividerHandle.PointerReleased += (_, e) => { _dragging = false; DividerHandle.ReleasePointerCapture(e.Pointer); Save(); };
        DividerHandle.PointerCaptureLost += (_, _) => { _dragging = false; Save(); };
        Root.Loaded += async (_, _) =>
        {
            StartupLog.Write("root-loaded");
            ApplyShellAppearance();
            if (_privacyCovered) RestorePrivacyFocus();
            if (_smoke && args.Contains("--smoke-privacy"))
            {
                await RunPrivacySmokeTest();
                return;
            }
            if (_smoke && args.Contains("--smoke-shell-layout"))
            {
                await RunShellLayoutSmokeTest();
                return;
            }
            if (_smoke && args.Contains("--smoke-quick-switch"))
            {
                await RunQuickSwitchSmokeTest();
                return;
            }
            if (_smoke && args.Contains("--smoke-quiet-web"))
            {
                await RunQuietWebSmokeTest();
                return;
            }
            if (_smoke && args.Contains("--smoke-quiet-ui"))
            {
                await RunQuietSmokeTest();
                return;
            }
            if (_smoke && args.Contains("--smoke-settings-ui"))
            {
                await RunSettingsSmokeTest();
                return;
            }
            if (_smoke && args.Contains("--smoke-signal-ui"))
            {
                await RunSignalVisualSmokeTest();
                return;
            }
            if (_smoke && args.Contains("--smoke-ui"))
            {
                await RunVisualSmokeTest();
                return;
            }
            if (_store.LoadWarning != null)
                ShowNotice("Settings need attention", _store.LoadWarning, InfoBarSeverity.Warning);
            StartupLog.Write("notification-backend=windows-tray available=" + _native.TrayAvailable);
            if (_state.Accounts.Count > 0)
            {
                _home = false;
                StartupLog.Write("restoring-layout");
                await RefreshLayout();
            }
            foreach (var a in _state.Accounts.Where(a => a.StartConnected).ToArray())
                await EnsureView(a);
            StartupLog.Write("startup-ready");
            if (_smoke)
                await RunSmokeTest();
            else if (args.Contains("--startup-check") && dataAt >= 0)
                await RunStartupCheck();
        };
    }
    public Task<CoreWebView2Environment> EnvironmentAsync() => _environment ??= CreateEnvironment();
    private async Task<CoreWebView2Environment> CreateEnvironment() => await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(_store.DirectoryPath, "Browser"), new CoreWebView2EnvironmentOptions());
    internal string AccountDataDirectory => _store.DirectoryPath;
    internal bool IsAccountActive(string id) => !_privacyCovered && !_home && FocusedId == id && _windowActive;
    private bool _windowActive;
    private IServiceSession CreateSession(Account a, bool diagnostic = false) => Providers.Find(a.ProviderId)!.Kind == ProviderKind.Signal
        ? new SignalServiceSession(a, this, diagnostic) : new ServiceView(a, this);
    private async Task<IServiceSession> EnsureView(Account a)
    {
        if (_views.TryGetValue(a.Id, out var existing))
            return existing;
        StartupLog.Write("creating-service " + a.ProviderId);
        var service = CreateSession(a);
        _views.Add(a.Id, service);
        service.View.Visibility = Visibility.Collapsed;
        ServiceLayer.Children.Add(service.View);
        StartupLog.Write("service-attached " + a.ProviderId);
        try
        {
            await service.InitializeAsync();
            StartupLog.Write("service-initialized " + a.ProviderId);
        }
        catch (Exception e) { Diagnostic("environment", e); ShowNotice("Unable to start browser", "Install or update Microsoft Edge WebView2 and restart Gather.", InfoBarSeverity.Error); }
        return service;
    }
    private async Task RefreshLayout()
    {
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }
        _refreshing = true;
        try
        {
            LayoutActions.Normalize(_state);
            Welcome.Visibility = _home || _state.Accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PaneArea.Visibility = Welcome.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            SplitButton.IsChecked = _state.Layout.Split && !_home;
            SwapButton.Visibility = _state.Layout.Split && !_home ? Visibility.Visible : Visibility.Collapsed;
            UpdateShellOverflow();
            PageTitle.Text = _home ? "Your space" : _state.Layout.Split ? "Side by side" : LeftAccount?.Name ?? "Your space";
            ToolTipService.SetToolTip(PageTitle, PageTitle.Text);
            if (_home)
            {
                PositionViews();
                UpdateStatus();
                return;
            }
            if (LeftAccount != null)
                await EnsureView(LeftAccount);
            if (_state.Layout.Split && RightAccount != null)
                await EnsureView(RightAccount);
            BuildPaneHeader(LeftHeader, false);
            BuildPaneHeader(RightHeader, true);
            ApplyColumns();
            PositionViews();
            UpdateFocus();
            if (FocusedId != null) QuickSwitch.Remember(_state, FocusedId);
            UpdateStatus();
            Save();
        }
        finally { _refreshing = false; if (_refreshAgain) { _refreshAgain = false; _ = RefreshLayout(); } }
    }
    private void ApplyColumns()
    {
        bool split = _state.Layout.Split && !_home;
        LeftColumn.Width = new(split ? _state.Layout.Ratio : 1, GridUnitType.Star);
        DividerColumn.Width = new(split ? Metrics.DividerWidth : 0);
        RightColumn.Width = split ? new(1 - _state.Layout.Ratio, GridUnitType.Star) : new(0);
        RightPane.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        PositionViews();
    }
    private void PositionViews()
    {
        // Keep each browser control attached to one parent for its entire life.
        // WinUI WebView2 tears down its browser when it is removed from the visual tree.
        foreach (var (id, service) in _views)
        {
            bool right = _state.Layout.Split && id == _state.Layout.Right;
            bool visible = !_privacyCovered && !_home && (id == _state.Layout.Left || right);
            service.View.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
                continue;
            service.View.HorizontalAlignment = HorizontalAlignment.Left;
            service.View.VerticalAlignment = VerticalAlignment.Stretch;
            service.View.Width = Math.Max(1, (right ? RightPane.ActualWidth : LeftPane.ActualWidth) - 2);
            service.View.Margin = new Thickness(right ? LeftPane.ActualWidth + DividerColumn.ActualWidth + 1 : 1, 1, 0, 1);
        }
    }
    public void FocusAccount(string id)
    {
        if (_quickSwitchOpen || _privacyCovered) return;
        if (_state.Layout.Split && id == _state.Layout.Right)
            _state.Layout.FocusRight = true;
        else if (id == _state.Layout.Left)
            _state.Layout.FocusRight = false;
        UpdateFocus();
        if (!_home && QuickSwitch.Remember(_state, id)) Save();
        AcknowledgeFocused();
    }
    private void UpdateFocus()
    {
        LeftPane.BorderBrush = _state.Layout.FocusRight ? Brush(IsDark ? "#3B454D" : "#D7DEDE") : Brush("#40AA90");
        RightPane.BorderBrush = _state.Layout.FocusRight ? Brush("#40AA90") : Brush(IsDark ? "#3B454D" : "#D7DEDE");
    }
    private void FocusWeb()
    {
        if (_privacyCovered) return;
        if (FocusedId != null && _views.TryGetValue(FocusedId, out var v))
            v.FocusContent();
    }
    private async void SelectAccount(string id, bool? right = null)
    {
        if (_privacyCovered) return;
        if (!_state.Accounts.Any(x => x.Id == id))
            return;
        LayoutActions.Select(_state.Layout, id, right);
        _home = false;
        await RefreshLayout();
        FocusWeb();
    }
    private void OpenBeside(string id)
    {
        if (_state.Accounts.Count < 2)
        {
            ShowNotice("Connect one more service", "Split view needs two service accounts.");
            return;
        }
        if (_state.Layout.Left == id)
            LayoutActions.EnableSplit(_state.Layout, _state.Accounts.Select(x => x.Id));
        else
        {
            _state.Layout.Right = id;
            _state.Layout.Split = true;
            _state.Layout.FocusRight = true;
        }
        _home = false;
        _ = RefreshLayout();
    }
    private void ToggleSplit()
    {
        if (_privacyCovered || _dialogs.CurrentCount == 0) return;
        if (_state.Layout.Split && !_home)
            LayoutActions.Collapse(_state.Layout);
        else if (!LayoutActions.EnableSplit(_state.Layout, _state.Accounts.Select(a => a.Id)))
        {
            SplitButton.IsChecked = false;
            ShowNotice("Add a second service", "Connect two service accounts to use split view.");
            return;
        }
        _home = false;
        _ = RefreshLayout();
    }
    internal void OnNotification(ServiceView service, CoreWebView2NotificationReceivedEventArgs e)
    {
        // Never delegate failure handling to the browser: it could expose message
        // text despite the user's private-preview preference.
        e.Handled = true;
        service.Unread.Receive();
        UpdateStatus();
        ShowServiceNotification(service.Account, e.Notification.Title, e.Notification.Body);
    }
    internal void ShowServiceNotification(Account account, string notificationTitle, string notificationBody)
    {
        if (_privacyCovered || QuietFor(account).SuppressNotifications || _suspending) return;
        string title = _state.HidePreviews ? account.Name : notificationTitle;
        string body = _state.HidePreviews ? "New activity — open Gather to view" : notificationBody;
        // Windows may suppress tray alerts according to the user's notification
        // settings. Acceptance is not proof of display, so don't report it shown.
        if (!(_smoke && _quietTestAlert != null ? _quietTestAlert(title, body) : _native.ShowAlert(title, body)))
            ShowNotice("Notifications unavailable", "Windows could not accept this alert. Open your services to check for messages.", InfoBarSeverity.Warning);
    }
    private bool TestNotification()
    {
        if (_privacyCovered) return false;
        const string title = "Gather notification test";
        const string body = "Notifications can reach you here. Click to open Gather.";
        return _native.ShowAlert(title, body);
    }
    internal void UpdateStatus()
    {
        var quiet = CurrentQuiet;
        foreach (var (id, badge) in _badges)
        {
            var value = _views.TryGetValue(id, out var v) ? v.Unread.Badge : default;
            var account = _state.Accounts.FirstOrDefault(a => a.Id == id);
            if (account != null && quiet.ForAccount(_state.Quiet, account).HideBadges) value = default;
            badge.Label.Text = value.Label("Number");
            badge.Background.Visibility = value.HasActivity ? Visibility.Visible : Visibility.Collapsed;
            string name = _state.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? "Service";
            string status = value.HasActivity ? value.Count > 0 ? $"{value.Label("Number")} unread" : "New activity" : "No reported unread activity";
            ToolTipService.SetToolTip(badge.Tile, name + " · " + status);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(badge.Tile, name + " · " + status);
        }
        var total = BadgeValue.Combine(_views.Values.Where(v => !v.Account.Muted && !quiet.ForAccount(_state.Quiet, v.Account).HideBadges).Select(v => v.Unread.Badge));
        _native.SetBadge(_privacyCovered ? "" : total.Label(_state.TaskbarBadge), _privacyCovered ? "Gather" : total.Count > 0 ? $"Gather: {total.Label("Number")} unread" : "Gather: new activity");
        var selected = !_home && FocusedId != null && _views.TryGetValue(FocusedId, out var focused) ? focused : null;
        StatusText.Text = selected == null ? $"{_state.Accounts.Count} service accounts · Stored on this computer" : $"{selected.Account.Name} · {selected.Status}";
        ToolTipService.SetToolTip(StatusText, StatusText.Text);
        UpdateQuietIndicator(quiet);
    }
    private void AcknowledgeFocused()
    {
        if (_privacyCovered) return;
        if (!_home && FocusedId != null && _views.TryGetValue(FocusedId, out var view))
            view.Acknowledge();
        UpdateStatus();
    }
    internal async void NavigateServices(int direction)
    {
        if (_privacyCovered) return;
        if (!LayoutActions.Navigate(_state.Layout, _state.Accounts.Select(a => a.Id).ToArray(), direction))
            return;
        _home = false;
        await RefreshLayout();
        FocusWeb();
    }
    private void Previous_Click(object sender, RoutedEventArgs e) => NavigateServices(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => NavigateServices(1);
    private void ApplyTheme()
    {
        Root.RequestedTheme = _state.Theme switch
        {
            "Dark" => ElementTheme.Dark,
            "System" => ElementTheme.Default,
            _ => ElementTheme.Light
        };
        RefreshTheme();
    }
    private void RefreshTheme()
    {
        BuildCards();
        UpdateFocus();
        ThemeButton.Content = IsDark ? "\uE706" : "\uE708";
        ToolTipService.SetToolTip(ThemeButton, IsDark ? "Switch to light mode" : "Switch to dark mode");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ThemeButton, IsDark ? "Switch to light mode" : "Switch to dark mode");
        var title = AppWindow.TitleBar;
        title.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        title.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        title.ButtonForegroundColor = IsDark ? Windows.UI.Color.FromArgb(255, 240, 244, 246) : Windows.UI.Color.FromArgb(255, 25, 30, 35);
        title.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 135, 145, 152);
        title.ButtonHoverBackgroundColor = IsDark ? Windows.UI.Color.FromArgb(255, 50, 58, 66) : Windows.UI.Color.FromArgb(255, 224, 231, 228);
        title.ButtonHoverForegroundColor = title.ButtonForegroundColor;
        foreach (var view in _views.Values)
            view.ApplyTheme();
        _quietLabel = null;
        UpdateStatus();
    }
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _state.Theme = IsDark ? "Light" : "Dark";
        ApplyTheme();
        Save();
    }
    private void Save()
    {
        try
        {
            _store.Save(_state);
        }
        catch (Exception e) { Diagnostic("settings-save", e); ShowNotice("Settings not saved", "Check available disk space and folder access.", InfoBarSeverity.Warning); }
    }
    internal void Diagnostic(string action, Exception e)
    {
        try
        {
            File.AppendAllText(Path.Combine(_store.DirectoryPath, "diagnostics.log"), $"{DateTimeOffset.UtcNow:O} {action} {e.GetType().Name} 0x{e.HResult:X8}\n");
        }
        catch { }
    }
    internal void DiagnosticEvent(string provider, string detail)
    {
        if (!_smoke && !DiagnosticMode)
            return;
        try
        {
            File.AppendAllText(Path.Combine(_store.DirectoryPath, "diagnostics.log"), $"{DateTimeOffset.UtcNow:O} {provider} {detail}\n");
        }
        catch { }
    }
    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var shortcut = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        shortcut.Invoked += (_, e) => { if (_dialogs.CurrentCount > 0 && !_quitting && !_privacyCovered) action(); e.Handled = true; };
        Root.KeyboardAccelerators.Add(shortcut);
    }
    internal void ShowWindow()
    {
        _native.Show();
        Activate();
    }
    private void Quit()
    {
        _quitting = true;
        Close();
    }
    private void Home_Click(object s, RoutedEventArgs e)
    {
        _home = true;
        _ = RefreshLayout();
    }
    private void Add_Click(object s, RoutedEventArgs e)
    {
        _home = true;
        _ = RefreshLayout();
    }
    private void Swap_Click(object s, RoutedEventArgs e)
    {
        LayoutActions.Swap(_state.Layout);
        _ = RefreshLayout();
    }
    private void Split_Click(object s, RoutedEventArgs e) => ToggleSplit();
    private void Divider_Drag(object s, ManipulationDeltaRoutedEventArgs e)
    {
        if (_dragging)
            return;
        _state.Layout.Ratio = Math.Clamp(_state.Layout.Ratio + e.Delta.Translation.X / Math.Max(1, PaneArea.ActualWidth), .3, .7);
        ApplyColumns();
        Save();
    }
    private void Divider_Reset(object s, DoubleTappedRoutedEventArgs e)
    {
        _state.Layout.Ratio = .5;
        ApplyColumns();
        Save();
    }
    private void Divider_KeyDown(object s, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Left or VirtualKey.Right)
        {
            _state.Layout.Ratio = Math.Clamp(_state.Layout.Ratio + (e.Key == VirtualKey.Left ? -.025 : .025), .3, .7);
            ApplyColumns();
            Save();
            e.Handled = true;
        }
    }
    private void SaveInitialState() => _store.Save(_state);
}


