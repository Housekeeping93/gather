using Gather.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
namespace Gather.App;

internal sealed partial class ServiceView : IServiceSession
{
    public Account Account
    {
        get;
    }
    public WebView2 View { get; } = new();
    FrameworkElement IServiceSession.View => View;
    public void FocusContent() => View.Focus(FocusState.Programmatic);
    public async Task InitializeAsync() => await InitializeAsync(await _host.EnvironmentAsync());
    public void Acknowledge() => Unread.Acknowledge();
    public async Task ClearDataAsync()
    {
        if (!Ready) throw new InvalidOperationException("Website is not ready to clear its data.");
        await View.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
    }
    public Provider Provider
    {
        get;
    }
    public string Status { get; private set; } = "Starting…";
    public UnreadState Unread { get; } = new();
    private bool _initialized;
    public bool Ready => _initialized && View.CoreWebView2 != null;
    public Task? Initialization
    {
        get; private set;
    }
    private readonly MainWindow _host;
    private bool _closed;
    private readonly List<(Window Window, WebView2 View)> _popups = [];
    public ServiceView(Account account, MainWindow host)
    {
        Account = account;
        Provider = Providers.Find(account.ProviderId)!;
        _host = host;
        View.GotFocus += (_, _) => host.FocusAccount(account.Id);
        ApplyTheme();
        View.Loaded += (_, _) => _host.DiagnosticEvent(Provider.Id, "view-loaded");
        View.Unloaded += (_, _) => _host.DiagnosticEvent(Provider.Id, "view-unloaded");
    }
    public Task InitializeAsync(CoreWebView2Environment environment) => Initialization ??= InitializeCoreAsync(environment);
    private async Task InitializeCoreAsync(CoreWebView2Environment environment)
    {
        try
        {
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = Account.ProfileName;
            options.IsInPrivateModeEnabled = false;
            await View.EnsureCoreWebView2Async(environment, options);
            if (_closed)
                return;
            Configure(View.CoreWebView2, false);
            View.CoreWebView2.DOMContentLoaded += async (_, _) => await ApplyContentSize();
            View.CoreWebView2.DocumentTitleChanged += (_, _) => { Unread.UpdateTitle(View.CoreWebView2.DocumentTitle); _host.UpdateStatus(); };
            View.CoreWebView2.NavigationStarting += (_, _) => { Status = "Loading…"; _host.UpdateStatus(); };
            View.CoreWebView2.NavigationCompleted += (_, e) => { Status = e.IsSuccess ? "Website ready" : "Could not load · Reload to retry"; _host.UpdateStatus(); };
            View.CoreWebView2.ProcessFailed += (_, e) => { _host.DiagnosticEvent(Provider.Id, $"process-failed {e.ProcessFailedKind} reason={e.Reason} exit={e.ExitCode}"); if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited) { if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited) _initialized = false; Status = "Service interrupted · Reload to retry"; _host.ShowNotice("Service interrupted", "The browser could not continue. Reload the service. If you started Gather in a restricted environment, open it normally from Windows.", InfoBarSeverity.Warning); } _host.UpdateStatus(); };

            _initialized = true;
            View.CoreWebView2.Navigate(NavigationPolicy.StartUrl(Account));
        }
        catch (Exception e) { Status = "Could not start this service"; _host.Diagnostic("service-init", e); _host.ShowNotice("Service unavailable", "Check that Microsoft Edge WebView2 is installed, then restart Gather.", InfoBarSeverity.Error); }
        _host.UpdateStatus();
    }
    private void Configure(CoreWebView2 core, bool popup)
    {
        core.IsMuted = _host.QuietFor(Account).MuteAudio;
        core.NotificationReceived += (_, e) => _host.OnNotification(this, e);
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreDevToolsEnabled = _host.DiagnosticMode;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Profile.IsPasswordAutosaveEnabled = false;
        core.Profile.IsGeneralAutofillEnabled = false;
        SetProfileTheme(core);
        core.PermissionRequested += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                if (NavigationPolicy.Decide(Provider, e.Uri) != NavigationDecision.Internal)
                {
                    e.State = CoreWebView2PermissionState.Deny;
                    return;
                }
                if (e.PermissionKind is CoreWebView2PermissionKind.Microphone or CoreWebView2PermissionKind.Camera or CoreWebView2PermissionKind.Geolocation)
                {
                    e.State = CoreWebView2PermissionState.Deny;
                    return;
                }
                e.State = await _host.RequestPermissionAsync(Account, e.PermissionKind.ToString()) ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            }
            catch (Exception ex) { e.State = CoreWebView2PermissionState.Deny; _host.Diagnostic("permission", ex); }
            finally { deferral.Complete(); }
        };
        core.NavigationStarting += (_, e) => { if ((popup || _quietDiagnostic) && e.Uri == "about:blank") return; var decision = NavigationPolicy.Decide(Provider, e.Uri); if (decision == NavigationDecision.Internal) return; e.Cancel = true; if (decision == NavigationDecision.IdentityBrowser) { _host.OfferExternalLogin(this); return; } if (decision == NavigationDecision.External && e.IsUserInitiated) _host.OpenExternal(e.Uri); else if (decision != NavigationDecision.Block) _host.OfferExternalPage(e.Uri); };
        core.LaunchingExternalUriScheme += (_, e) => { e.Cancel = true; _host.ShowNotice("Stay in Gather", "Use the service’s web version to continue here. Desktop-app links are not opened automatically."); };
        core.NewWindowRequested += async (_, e) =>
        {
            e.Handled = true;
            var decision = NavigationPolicy.Decide(Provider, e.Uri);
            if (decision == NavigationDecision.IdentityBrowser)
            {
                _host.OfferExternalLogin(this);
                return;
            }
            if (decision == NavigationDecision.External)
            {
                if (e.IsUserInitiated)
                    _host.OpenExternal(e.Uri);
                return;
            }
            if (decision != NavigationDecision.Internal && !(e.Uri == "about:blank" && e.IsUserInitiated))
                return;
            if (_popups.Count >= 3)
                return;
            var deferral = e.GetDeferral();
            try
            {
                var web = new WebView2();
                var window = new Window { Title = Account.Name + " — Gather", Content = web };
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 760));
                window.Activate();
                var options = (await _host.EnvironmentAsync()).CreateCoreWebView2ControllerOptions();
                options.ProfileName = Account.ProfileName;
                await web.EnsureCoreWebView2Async(await _host.EnvironmentAsync(), options);
                Configure(web.CoreWebView2, true);
                e.NewWindow = web.CoreWebView2;
                _popups.Add((window, web));
                window.Closed += (_, _) => { web.Close(); _popups.RemoveAll(x => x.Window == window); };
            }
            catch (Exception ex) { _host.Diagnostic("popup", ex); }
            finally { deferral.Complete(); }
        };
    }
    public void Reload()
    {
        if (Ready)
            View.CoreWebView2.Reload();
    }
    public async Task SetContentSize(double zoom)
    {
        Account.Zoom = Math.Clamp(zoom, .75, 1.5);
        await ApplyContentSize();
    }
    private async Task ApplyContentSize()
    {
        if (View.CoreWebView2 == null)
            return;
        try
        {
            string value = Account.Zoom.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await View.CoreWebView2.ExecuteScriptAsync("document.documentElement.style.zoom='" + value + "'");
        }
        catch (Exception ex) { _host.Diagnostic("content-size", ex); }
    }
    public void RestartSignIn()
    {
        if (Ready)
            View.CoreWebView2.Navigate(NavigationPolicy.FreshSignInUrl(Account));
    }
    private void SetProfileTheme(CoreWebView2 core) => core.Profile.PreferredColorScheme = _host.ThemePreference switch
    {
        "Dark" => CoreWebView2PreferredColorScheme.Dark,
        "Light" => CoreWebView2PreferredColorScheme.Light,
        _ => CoreWebView2PreferredColorScheme.Auto
    };
    public void ApplyTheme()
    {
        View.DefaultBackgroundColor = _host.IsDark ? Windows.UI.Color.FromArgb(255, 24, 28, 32) : Windows.UI.Color.FromArgb(255, 250, 251, 253);
        if (View.CoreWebView2 != null)
            SetProfileTheme(View.CoreWebView2);
        foreach (var popup in _popups)
        {
            popup.View.DefaultBackgroundColor = View.DefaultBackgroundColor;
            popup.View.RequestedTheme = _host.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        }
    }
    public void SetAudioMuted(bool muted)
    {
        if (_closed) return;
        if (View.CoreWebView2 is { } core && core.IsMuted != muted) core.IsMuted = muted;
        foreach (var popup in _popups)
            if (popup.View.CoreWebView2 is { } child && child.IsMuted != muted) child.IsMuted = muted;
    }
    public void Close()
    {
        _closed = true;
        _initialized = false;
        foreach (var p in _popups.ToArray())
            p.Window.Close();
        View.Close();
    }
}

