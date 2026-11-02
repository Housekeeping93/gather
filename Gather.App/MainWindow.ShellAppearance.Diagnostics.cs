using Gather.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Text.Json;

namespace Gather.App;

public sealed partial class MainWindow
{
    private async Task RunShellLayoutSmokeTest()
    {
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        async Task Settle() { Root.UpdateLayout(); await Task.Delay(400); }
        async Task WaitFor(Func<bool> test)
        {
            for (int i = 0; i < 100 && !test(); i++) await Task.Delay(50);
            Require(test(), "Shell layout diagnostic timed out.");
        }
        var cases = new List<object>();
        try
        {
            Require(_state.Accounts.Count == 0, "Use a fresh shell-layout data directory.");
            foreach (string name in new[] { "Family Signal", "Work Signal with a deliberately long account name" })
            {
                var account = new Account { ProviderId = "signal", Name = name, StartConnected = false, Zoom = 1.25, Muted = true };
                _state.Accounts.Add(account);
                var session = new SignalServiceSession(account, this, diagnostic: true);
                session.EnableQuickSwitchDiagnosticInput();
                _views.Add(account.Id, session); ServiceLayer.Children.Add(session.View);
            }
            var ids = _state.Accounts.Select(a => a.Id).ToArray();
            _state.Layout.Left = ids[0]; _state.Layout.Right = ids[1]; _state.Layout.Split = true;
            _state.Layout.FocusRight = true; _state.Layout.Ratio = .35; _home = false;
            _state.Theme = "Dark"; ApplyTheme();
            BuildRail(); await RefreshLayout(); StartFocus(25);
            var focusUntil = _state.Quiet.FocusUntil;
            double scale = Root.XamlRoot.RasterizationScale;
            foreach (var density in new[] { "Compact", "Comfortable" })
            foreach (var size in new[] { (Width: 1360, Height: 900), (Width: 960, Height: 640) })
            {
                _state.ShellDensity = density; _state.FocusView = false;
                AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(size.Width * scale), (int)(size.Height * scale)));
                ApplyShellAppearance(); await Settle(); FocusWeb();
                var metrics = Metrics;
                void CheckBounds()
                {
                    Require(Math.Abs(LeftColumn.ActualWidth + DividerColumn.ActualWidth + RightColumn.ActualWidth - PaneArea.ActualWidth) < 2, "Pane columns do not cover the content area.");
                    Require(Math.Abs(DividerColumn.ActualWidth - Metrics.DividerWidth) < 1, "Divider does not match the selected density.");
                    Require(Math.Abs(_views[ids[0]].View.ActualWidth - (LeftPane.ActualWidth - 2)) < 2, "Left session is misaligned.");
                    Require(Math.Abs(_views[ids[1]].View.Margin.Left - (LeftPane.ActualWidth + DividerColumn.ActualWidth + 1)) < 2, "Right session is misaligned.");
                    Require(Math.Abs(ServiceLayer.ActualHeight - (PaneArea.ActualHeight - Metrics.PaneHeaderHeight)) < 2, "Session height does not match the pane headers.");
                    Require(_views.Count == 2 && ServiceLayer.Children.Count == 2, "A layout change recreated or duplicated a session.");
                }
                CheckBounds();
                Require(ShellTitleRow.Height.Value == metrics.TitleHeight && RailColumn.Width.Value == metrics.RailWidth, "Density was not applied to the title/sidebar.");
                Require(_badges[ids[0]].Tile.Width == metrics.TileSize, "Account controls did not resize.");
                await CaptureShell("shell-" + density.ToLowerInvariant() + "-" + size.Width);
                ToggleFocusView(); await Settle(); CheckBounds();
                Require(RailBorder.Visibility == Visibility.Collapsed && LeftHeader.Visibility == Visibility.Collapsed && ToolbarActions.Visibility == Visibility.Collapsed, "Secondary shell controls remain exposed in focus view.");
                Require(ExitFocusViewButton.Visibility == Visibility.Visible && FocusToolbar.Visibility == Visibility.Visible && ExitFocusViewButton.IsTabStop, "Exit focus view is not reachable.");
                Require(AutomationProperties.GetName(ExitFocusViewButton).Contains("Exit focus view"), "Exit focus view lacks an accessible name.");
                var exitBounds = ExitFocusViewButton.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
                Require(exitBounds.X >= 0 && exitBounds.X + ExitFocusViewButton.ActualWidth <= Root.ActualWidth + 1, "Exit focus button is clipped.");
                Require(_state.Quiet.FocusUntil == focusUntil && _state.Accounts.All(a => a.Muted && a.Zoom == 1.25), "Layout changed notification policy, mute or provider zoom.");
                await CaptureShell("shell-focus-" + density.ToLowerInvariant() + "-" + size.Width);
                ToggleFocusView(); await Settle(); CheckBounds();
                Require(RailBorder.Visibility == Visibility.Visible && _state.ShellDensity == density && _state.Layout.FocusRight && _state.Layout.Ratio == .35, "Focus view did not restore the layout.");
                cases.Add(new { density, size.Width, size.Height, actualWidth = Root.ActualWidth, actualHeight = Root.ActualHeight, scale, normalAndFocusBoundsPass = true });
            }
            _state.Theme = "Light"; ApplyTheme(); await Settle(); await CaptureShell("shell-comfortable-light");
            var originalDensity = _state.ShellDensity;
            var pending = ShowSettings(); await Task.Delay(350);
            DialogElements(_activeDialog!).OfType<ComboBox>().Single(c => c.Name == "ShellDensity").SelectedItem = "Compact";
            InvokeSettingsButton("Cancel"); await pending;
            Require(_state.ShellDensity == originalDensity, "Settings Cancel changed shell density.");
            pending = ShowSettings(); await Task.Delay(350);
            DialogElements(_activeDialog!).OfType<ComboBox>().Single(c => c.Name == "ShellDensity").SelectedItem = "Comfortable";
            DialogElements(_activeDialog!).OfType<ToggleSwitch>().Single(c => c.Name == "FocusView").IsOn = true;
            await CaptureShell("shell-settings-light", _activeDialog);
            InvokeSettingsButton("Save"); await pending; await Settle();
            var read = _store.Load();
            Require(read.ShellDensity == "Comfortable" && read.FocusView && read.Accounts[0].ProfileName == _state.Accounts[0].ProfileName, "Shell preferences did not survive save/reload.");
            RequestQuickSwitch(); await WaitFor(() => _activeDialog != null); await Task.Delay(250);
            Require(_quickSwitchOpen, "Quick switch is unavailable in focus view.");
            InvokeSettingsButton("Cancel"); await WaitFor(() => !_quickSwitchOpen);
            ToggleFocusView(); await Settle();
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "shell-layout-report.json"), JsonSerializer.Serialize(new
            {
                cases, settingsCancelPass = true, persistencePass = true, quickSwitchInFocusPass = true,
                zoomAndMutePreserved = true, sessionReusePass = true, quietPolicyPreserved = true,
                accessibleRestorePass = true, nativeSignalSessionsConnected = 0, providerSessionsOpened = 0,
                physicalShortcutTested = false, otherDisplayScalesTested = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Diagnostic("shell-layout-smoke", ex);
            File.WriteAllText(Path.Combine(_store.DirectoryPath, "shell-layout-failure.txt"), ex.ToString());
        }
        finally { _activeDialog?.Hide(); Quit(); }
    }
}
