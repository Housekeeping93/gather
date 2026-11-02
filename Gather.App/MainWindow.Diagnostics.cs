using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Gather.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.Win32;
namespace Gather.App;

public sealed partial class MainWindow
{
    private async Task RunSettingsSmokeTest()
    {
        string keyPath = @"Software\Gather-Startup-UI-Tests-" + Guid.NewGuid().ToString("N");
        var registration = new StartupRegistration(keyPath);
        try
        {
            if (_state.Accounts.Count != 0) throw new InvalidOperationException("Use an empty settings-check profile.");
            _state.Theme = "Dark";
            ApplyTheme();
            var pending = ShowSettings(registration);
            await Task.Delay(450);
            StartupToggle().IsOn = true;
            InvokeSettingsButton("Cancel");
            await pending;
            bool cancelPass = registration.ReadCommand() == null;

            pending = ShowSettings(registration);
            await Task.Delay(350);
            bool initiallyOff = !StartupToggle().IsOn;
            StartupToggle().IsOn = true;
            await Task.Delay(300);
            await CaptureShell("settings-startup-dark", _activeDialog);
            InvokeSettingsButton("Save");
            await pending;
            bool enablePass = registration.ReadCommand() == StartupRegistration.CommandLine(System.Environment.ProcessPath!, _store.DirectoryPath);

            _state.Theme = "Light";
            ApplyTheme();
            pending = ShowSettings(registration);
            await Task.Delay(350);
            bool reloadPass = StartupToggle().IsOn;
            StartupToggle().IsOn = false;
            await Task.Delay(300);
            await CaptureShell("settings-startup-light", _activeDialog);
            InvokeSettingsButton("Save");
            await pending;
            bool disablePass = registration.ReadCommand() == null;
            if (!cancelPass || !initiallyOff || !enablePass || !reloadPass || !disablePass)
                throw new InvalidOperationException("Startup settings interaction failed.");
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "settings-report.json"), JsonSerializer.Serialize(new
            {
                cancelPass, initiallyOff, enablePass, reloadPass, disablePass,
                realStartupEntriesChanged = 0, accountsOpened = _views.Count
            }));
        }
        catch (Exception error) { Diagnostic("settings-smoke", error); }
        finally
        {
            _activeDialog?.Hide();
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            Quit();
        }
    }

    private ToggleSwitch StartupToggle() => DialogElements(_activeDialog!).OfType<ToggleSwitch>().Single(x => x.Name == "StartWithWindowsToggle");
    private void InvokeSettingsButton(string label)
    {
        var button = DialogElements(_activeDialog!).OfType<Button>().Single(x => x.Content is string content && content == label);
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static IEnumerable<DependencyObject> DialogElements(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in DialogElements(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }

    private async Task RunSignalVisualSmokeTest()
    {
        try
        {
            if (_state.Accounts.Count != 0) throw new InvalidOperationException("Use an empty Signal visual-check profile.");
            for (int i = 0; i < 2; i++)
            {
                var account = new Account { ProviderId = "signal", Name = "Signal sample " + (i + 1), StartConnected = false };
                _state.Accounts.Add(account);
                var session = CreateSession(account, diagnostic: true);
                _views.Add(account.Id, session); ServiceLayer.Children.Add(session.View);
            }
            var first = (SignalServiceSession)_views[_state.Accounts[0].Id];
            bool signalAvatarPass = await first.ShowDiagnosticAvatar();
            if (!signalAvatarPass) throw new InvalidOperationException("Signal avatar decode failed.");
            BuildRail(); _home = false; _state.Theme = "Dark"; ApplyTheme();
            LayoutActions.EnableSplit(_state.Layout, _state.Accounts.Select(a => a.Id));
            await RefreshLayout(); await Task.Delay(300); await CaptureShell("signal-split-dark");
            bool signalSplitPass = _state.Layout.Split && _views.Values.All(v => v.View.Visibility == Visibility.Visible);
            LayoutActions.Swap(_state.Layout); await RefreshLayout();
            bool signalSwapPass = _state.Layout.Right == _state.Accounts[0].Id && ReferenceEquals(first, _views[_state.Layout.Right]);
            _state.Layout.FocusRight = true; LayoutActions.Collapse(_state.Layout);
            _state.Theme = "Light"; ApplyTheme(); await RefreshLayout(); await Task.Delay(300); await CaptureShell("signal-single-light");
            bool signalSinglePass = !_state.Layout.Split && _state.Layout.Left == first.Account.Id;
            if (!signalSplitPass || !signalSwapPass || !signalSinglePass) throw new InvalidOperationException("Signal pane placement failed.");
            bool compactLayoutPass = PaneBoundsFit();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 640));
            _state.Accounts[0].Name = "A long service account name to check compact headers";
            LayoutActions.EnableSplit(_state.Layout, _state.Accounts.Select(a => a.Id));
            _state.Layout.Ratio = .3;
            await RefreshLayout(); await Task.Delay(300);
            compactLayoutPass &= PaneBoundsFit();
            await CaptureShell("compact-split-light");
            _state.Theme = "Dark"; ApplyTheme();
            _state.Layout.Ratio = .7;
            await RefreshLayout(); await Task.Delay(300);
            compactLayoutPass &= PaneBoundsFit();
            await CaptureShell("compact-split-dark");
            if (!compactLayoutPass) throw new InvalidOperationException("Compact service bounds do not align with the pane surfaces.");
            _home = true; await RefreshLayout(); await Task.Delay(300); await CaptureShell("compact-home-dark");
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "visual-report.json"), JsonSerializer.Serialize(new
                { signalAvatarPass, signalSplitPass, signalSwapPass, signalSinglePass, compactLayoutPass, signalSessionsConnected = 0, browserSessionsOpened = 0 }));
        }
        catch (Exception error) { Diagnostic("signal-visual-smoke", error); }
        finally { Quit(); }
    }
    private bool PaneBoundsFit()
    {
        foreach (var service in _views.Values.Where(v => v.View.Visibility == Visibility.Visible))
        {
            var pane = _state.Layout.Split && service.Account.Id == _state.Layout.Right ? RightPane : LeftPane;
            var surface = ReferenceEquals(pane, RightPane) ? RightSurface : LeftSurface;
            var actual = service.View.TransformToVisual(PaneArea).TransformPoint(new(0, 0));
            var expected = surface.TransformToVisual(PaneArea).TransformPoint(new(0, 0));
            if (Math.Abs(actual.X - expected.X) > 1 || Math.Abs(actual.Y - expected.Y) > 1 ||
                Math.Abs(service.View.ActualWidth - surface.ActualWidth) > 1 ||
                Math.Abs(service.View.ActualHeight - surface.ActualHeight) > 1)
                return false;
        }
        return true;
    }
    private async Task RunStartupCheck()
    {
        try
        {
            await Task.Delay(2000);
            await CaptureShell("startup-shell");
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "startup-report.json"), JsonSerializer.Serialize(new
            {
                ready = true,
                accounts = _state.Accounts.Count,
                views = _views.Count,
                sessions = _views.Values.Select(v => new { provider = v.Account.ProviderId, v.Ready, v.Status }),
                notificationBackend = "windows-tray",
                trayAvailable = _native.TrayAvailable
            }));
        }
        catch (Exception e) { Diagnostic("startup-check", e); }
        finally { Quit(); }
    }

    private async Task RunVisualSmokeTest()
    {
        try
        {
            if (_state.Accounts.Count != 0)
                throw new InvalidOperationException("Visual smoke checks require a fresh, empty data directory.");
            int[] counts = [1, 4, 50, 109, 0, 2];
            for (int i = 0; i < Providers.All.Length; i++)
            {
                var provider = Providers.All[i];
                StartupLog.Write("visual-service-create " + provider.Id);
                var account = new Account { ProviderId = provider.Id, Name = provider.Name, StartConnected = false };
                _state.Accounts.Add(account);
                var view = CreateSession(account, diagnostic: true);
                StartupLog.Write("visual-service-created " + provider.Id);
                ServiceLayer.Children.Add(view.View);
                if (counts[i] > 0)
                    view.Unread.UpdateTitle($"({counts[i]}) {provider.Name}");
                else
                    view.Unread.Receive();
                _views.Add(account.Id, view); // No browser initialization or provider traffic.
            }
            BuildRail();
            _state.Theme = "Light";
            ApplyTheme();
            await Task.Delay(500);
            await CaptureShell("home-light");
            _state.Theme = "Dark";
            ApplyTheme();
            await Task.Delay(500);
            await CaptureShell("home-dark");
            LayoutActions.EnableSplit(_state.Layout, _state.Accounts.Select(a => a.Id));
            _home = false;
            await RefreshLayout();
            await Task.Delay(500);
            await CaptureShell("split-dark");
            var order = _state.Accounts.Select(a => a.Id).ToArray();
            Next_Click(this, new RoutedEventArgs());
            await Task.Delay(200);
            bool nextPass = _state.Layout.Left == order[1] && _state.Layout.Right == order[2];
            Previous_Click(this, new RoutedEventArgs());
            await Task.Delay(200);
            bool previousPass = _state.Layout.Left == order[0] && _state.Layout.Right == order[1];
            if (!nextPass || !previousPass)
                throw new InvalidOperationException("Navigation button check failed.");
            ReorderAccount(order[2], 0);
            bool reorderPass = _state.Accounts[0].Id == order[2];
            var signalAccount = _state.Accounts.Single(a => a.ProviderId == "signal");
            var signalSession = _views[signalAccount.Id];
            bool signalAvatarPass = await ((SignalServiceSession)signalSession).ShowDiagnosticAvatar();
            if (!signalAvatarPass) throw new InvalidOperationException("Signal avatar rendering check failed.");
            _state.Layout.Right = signalAccount.Id;
            _state.Layout.Split = true;
            _state.Layout.FocusRight = true;
            await RefreshLayout();
            await Task.Delay(300);
            await CaptureShell("signal-split-dark");
            bool signalSplitPass = signalSession.View.Visibility == Visibility.Visible && _state.Layout.Right == signalAccount.Id;
            LayoutActions.Swap(_state.Layout);
            await RefreshLayout();
            bool signalSwapPass = ReferenceEquals(signalSession, _views[signalAccount.Id]) && _state.Layout.Left == signalAccount.Id;
            LayoutActions.Collapse(_state.Layout);
            _state.Theme = "Light";
            ApplyTheme();
            await RefreshLayout();
            await Task.Delay(300);
            await CaptureShell("signal-single-light");
            bool signalSinglePass = !_state.Layout.Split && _state.Layout.Left == signalAccount.Id && signalSession.View.Visibility == Visibility.Visible;
            if (!signalSplitPass || !signalSwapPass || !signalSinglePass) throw new InvalidOperationException("Signal pane placement check failed.");
            _state.Theme = "Dark";
            ApplyTheme();
            _home = true;
            await RefreshLayout();
            Settings_Click(this, new RoutedEventArgs());
            await Task.Delay(500);
            await CaptureShell("settings-dark", _activeDialog);
            foreach (var label in new[] { "4", "54", "99+", "•" })
            {
                var dib = TaskbarBadge.IconBytes(label);
                var pixels = new byte[32 * 32 * 4];
                for (int y = 0; y < 32; y++)
                    Array.Copy(dib, 40 + (31 - y) * 128, pixels, y * 128, 128);
                using var stream = File.Create(Path.Combine(_store.DirectoryPath, "badge-" + (label == "•" ? "dot" : label) + ".png"));
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream());
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 32, 32, 96, 96, pixels);
                await encoder.FlushAsync();
            }
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "visual-report.json"), JsonSerializer.Serialize(new
            {
                nextPass,
                previousPass,
                reorderPass,
                signalSplitPass,
                signalSwapPass,
                signalSinglePass,
                signalAvatarPass,
                signalSessionsConnected = 0,
                theme = Root.ActualTheme.ToString(),
                badgeMode = _state.TaskbarBadge,
                taskbarResult = _native.BadgeResult,
                navigationKeysInstalled = _navigationKeys.Available,
                browserSessionsOpened = 0
            }));
        }
        catch (Exception ex) { Diagnostic("visual-smoke", ex); }
        finally { Quit(); }
    }
    private async Task CaptureShell(string name, FrameworkElement? target = null)
    {
        try
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(target ?? Root);
            var pixels = await bitmap.GetPixelsAsync();
            var bytes = new byte[pixels.Length];
            using (var reader = DataReader.FromBuffer(pixels))
                reader.ReadBytes(bytes);
            using var stream = File.Create(Path.Combine(_store.DirectoryPath, name + ".png"));
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream());
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
            await encoder.FlushAsync();
        }
        catch (Exception e) { Diagnostic("capture-shell", e); }
    }
    private async Task RunSmokeTest()
    {
        try
        {
            await Task.Delay(_views.Count > 0 ? 12000 : 2000);
            await CaptureShell("shell");
            var serviceReports = new List<object>();
            foreach (var view in _views.Values)
            {
                string features = "{}";
                if (view is ServiceView web && web.Ready && web.View.CoreWebView2 != null)
                {
                    features = await web.View.CoreWebView2.ExecuteScriptAsync("JSON.stringify({origin:location.origin,ready:document.readyState,loginText:/log in|sign in|link.*device|scan.*code|aanmelden/i.test(document.body.innerText),hasCanvas:!!document.querySelector('canvas'),hasInput:!!document.querySelector('input')})");
                    try
                    {
                        using var stream = File.Create(Path.Combine(_store.DirectoryPath, view.Account.ProviderId + ".png"));
                        await web.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream.AsRandomAccessStream());
                    }
                    catch (Exception ex) { Diagnostic("capture-" + view.Account.ProviderId, ex); }
                }
                serviceReports.Add(new
                {
                    provider = view.Account.ProviderId,
                    view.Ready,
                    view.Status,
                    view.Account.ProfileName,
                    features
                });
            }
            var report = new
            {
                stage = "native-and-webviews",
                hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this).ToInt64(),
                accounts = _state.Accounts.Count,
                views = _views.Count,
                split = _state.Layout.Split,
                tray = _native.TrayAvailable,
                privateBytes = Process.GetCurrentProcess().PrivateMemorySize64,
                workingSet = Process.GetCurrentProcess().WorkingSet64,
                services = serviceReports
            };
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "smoke-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Diagnostic("smoke-test", e); }
        finally { Quit(); }
    }
}

