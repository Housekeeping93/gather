using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Gather.App;
internal sealed partial class ServiceView
{
    private bool _quietDiagnostic;
    internal async Task InitializeQuietDiagnosticAsync()
    {
        var args = System.Environment.GetCommandLineArgs();
        if (!args.Contains("--smoke-quiet-web") && !args.Contains("--smoke-quick-switch") && !args.Contains("--smoke-privacy")) throw new InvalidOperationException("Offline diagnostic only.");
        _quietDiagnostic = true;
        var environment = await _host.EnvironmentAsync();
        var options = environment.CreateCoreWebView2ControllerOptions(); options.ProfileName = Account.ProfileName;
        await View.EnsureCoreWebView2Async(environment, options);
        if (View.CoreWebView2 == null) throw new InvalidOperationException("WebView2 did not create an offline browser controller in this session.");
        Configure(View.CoreWebView2, false);
        _initialized = true;
        View.CoreWebView2.NavigateToString("<html><body>Offline quiet-hours check. No service connected.</body></html>");
    }
    internal async Task<WebView2> CreateQuietDiagnosticPopupAsync()
    {
        if (!_quietDiagnostic) throw new InvalidOperationException("Offline diagnostic only.");
        var web = new WebView2();
        var window = new Window { Title = "Offline popup check", Content = web };
        window.Activate();
        _host.RegisterPrivacyPopup(window);
        var environment = await _host.EnvironmentAsync();
        var options = environment.CreateCoreWebView2ControllerOptions(); options.ProfileName = Account.ProfileName;
        await web.EnsureCoreWebView2Async(environment, options);
        Configure(web.CoreWebView2, true);
        _popups.Add((window, web));
        window.Closed += (_, _) => { web.Close(); _popups.RemoveAll(x => x.Window == window); };
        web.CoreWebView2.NavigateToString("<html><body>Offline popup. No service connected.</body></html>");
        return web;
    }
}
