using Gather.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Gather.App;

public sealed partial class MainWindow
{
    private ShellMetrics Metrics => ShellAppearance.For(_state.ShellDensity, _state.FocusView);
    private string? _appliedDensity;
    private bool _focusToggleQueued;

    private void InitializeShellAppearance()
    {
        var menu = new MenuFlyout();
        MenuItem(menu, "Previous service", () => NavigateServices(-1));
        MenuItem(menu, "Next service", () => NavigateServices(1));
        MenuItem(menu, "Swap sides", () => { LayoutActions.Swap(_state.Layout); _ = RefreshLayout(); });
        MenuItem(menu, "Toggle split view (Alt+F)", ToggleSplit);
        MenuItem(menu, "Focus timer and quiet hours", () => OpenQuietMenu(ToolbarMoreButton));
        MenuItem(menu, "Switch light/dark theme", () => Theme_Click(ThemeButton, new RoutedEventArgs()));
        MenuItem(menu, "Layout and settings", async () => await ShowSettings());
        ToolbarMoreButton.Flyout = menu;
        FocusQuietButton.Click += (_, _) => OpenQuietMenu(FocusQuietButton);
        ApplyShellAppearance();
    }

    private void ApplyShellAppearance()
    {
        var m = Metrics;
        ShellTitleRow.Height = new(m.TitleHeight);
        ToolbarRow.Height = new(m.ToolbarHeight);
        RailColumn.Width = new(m.RailWidth);
        RailBorder.Visibility = _state.FocusView ? Visibility.Collapsed : Visibility.Visible;
        ShellContent.Padding = new(m.OuterSpacing, 0, m.OuterSpacing, m.OuterSpacing);
        ToolbarActions.Visibility = _state.FocusView ? Visibility.Collapsed : Visibility.Visible;
        FocusToolbar.Visibility = _state.FocusView ? Visibility.Visible : Visibility.Collapsed;
        LeftPaneHeaderRow.Height = RightPaneHeaderRow.Height = new(m.PaneHeaderHeight);
        LeftHeader.Visibility = RightHeader.Visibility = _state.FocusView ? Visibility.Collapsed : Visibility.Visible;
        LeftHeader.Padding = RightHeader.Padding = new(_state.ShellDensity == "Comfortable" ? 10 : 6, 4, 6, 4);
        ServiceLayer.Margin = new(0, m.PaneHeaderHeight, 0, 0);
        DividerHandle.Width = m.DividerWidth;
        DividerHandle.Height = _state.ShellDensity == "Comfortable" ? 64 : 54;
        foreach (var control in new Control[] { QuickSwitchButton, FocusViewButton, ToolbarMoreButton, PreviousButton,
            NextButton, QuietButton, ThemeButton, SwapButton, SplitButton, FocusQuickSwitchButton, FocusQuietButton, ExitFocusViewButton, PrivacyButton, FocusPrivacyButton })
        {
            control.Height = m.ButtonHeight; control.MinWidth = m.ButtonHeight;
            control.Padding = new(_state.ShellDensity == "Comfortable" ? 10 : 8, 0, _state.ShellDensity == "Comfortable" ? 10 : 8, 0);
        }
        foreach (var button in new[] { HomeButton, AddServiceButton, SettingsButton })
        {
            button.Width = m.TileSize - 4; button.Height = m.ButtonHeight + 6;
        }
        ServiceRail.Spacing = _state.ShellDensity == "Comfortable" ? 10 : 6;
        PageTitle.FontSize = _state.ShellDensity == "Comfortable" ? 16 : 15;
        if (_appliedDensity != _state.ShellDensity)
        {
            _appliedDensity = _state.ShellDensity;
            BuildRail();
        }
        BuildPaneHeader(LeftHeader, false); BuildPaneHeader(RightHeader, true);
        ApplyColumns(); UpdateShellOverflow();
    }

    private void UpdateShellOverflow()
    {
        if (ToolbarMoreButton == null) return; // SizeChanged can run during XAML initialization.
        bool narrow = Root.ActualWidth > 0 && Root.ActualWidth < 1000;
        ToolbarLabels.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Visibility = _state.FocusView ? Visibility.Collapsed : Visibility.Visible;
        PageTitle.MaxWidth = Root.ActualWidth < 1200 ? 120 : 240;
        ToolbarMoreButton.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { PreviousButton, NextButton, ThemeButton })
            button.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        SplitButton.Visibility = Root.ActualWidth > 0 && Root.ActualWidth < 600 ? Visibility.Collapsed : Visibility.Visible;
        SwapButton.Visibility = !narrow && !_home && _state.Layout.Split ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool RequestFocusView()
    {
        if (_privacyCovered) return true;
        if (_quitting || _dialogs.CurrentCount == 0) return false;
        if (_focusToggleQueued) return true;
        _focusToggleQueued = true;
        if (DispatcherQueue.TryEnqueue(() => { _focusToggleQueued = false; ToggleFocusView(); })) return true;
        _focusToggleQueued = false;
        return false;
    }

    private void ToggleFocusView()
    {
        if (_quitting || _dialogs.CurrentCount == 0 || _privacyCovered) return;
        _state.FocusView = !_state.FocusView;
        ApplyShellAppearance(); Save();
        if (!_home) FocusWeb();
        else if (_state.FocusView) ExitFocusViewButton.Focus(FocusState.Programmatic);
        else FocusViewButton.Focus(FocusState.Programmatic);
    }
    private void FocusView_Click(object sender, RoutedEventArgs e) => ToggleFocusView();
}
