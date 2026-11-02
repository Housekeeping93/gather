using Gather.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Gather.App;

public sealed partial class MainWindow
{
    private bool _privacyCovered;
    private readonly HashSet<Window> _privacyPopups = [];
    private readonly HashSet<Window> _privacyHiddenPopups = [];
    internal bool IsPrivacyCovered => _privacyCovered;

    private bool OwnsShortcutWindow(nint window) => window == WinRT.Interop.WindowNative.GetWindowHandle(this)
        || _privacyPopups.Any(p => WinRT.Interop.WindowNative.GetWindowHandle(p) == window);

    private void InitializePrivacy()
    {
        foreach (var choice in PrivacyCurtain.Shortcuts)
        {
            string shortcut = choice;
            var accelerator = new KeyboardAccelerator { Key = (VirtualKey)shortcut[^1], Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
            accelerator.Invoked += (_, e) =>
            {
                if (_state.PrivacyShortcut == shortcut) { RequestPrivacy(); e.Handled = true; }
            };
            Root.KeyboardAccelerators.Add(accelerator);
        }
        UpdatePrivacyShortcut();
        if (_state.PrivacyCovered) SetPrivacyCovered(true);
    }

    private void UpdatePrivacyShortcut()
    {
        string label = "Hide conversations (" + _state.PrivacyShortcut + ")";
        foreach (var button in new[] { PrivacyButton, FocusPrivacyButton })
        {
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetName(button, label);
        }
        PrivacyShortcutHint.Text = "Press " + _state.PrivacyShortcut + " to hide or show Gather.";
    }

    private bool RequestPrivacy()
    {
        if (_quitting) return false;
        // Apply immediately on the UI thread, including inside dialogs and browser popups.
        SetPrivacyCovered(!_privacyCovered);
        return true;
    }

    private bool RequestSplitView()
    {
        if (_privacyCovered) return true;
        if (_quitting || _dialogs.CurrentCount == 0) return false;
        ToggleSplit();
        return true;
    }

    private void SetPrivacyCovered(bool covered)
    {
        if (_privacyCovered == covered) return;
        _privacyCovered = _state.PrivacyCovered = covered;
        if (covered)
        {
            // Collapse, rather than detach: WebView2 retains its session and drafts.
            PrivateShell.Visibility = Visibility.Collapsed;
            PrivateShell.IsHitTestVisible = false;
            PrivacyCover.Visibility = Visibility.Visible;
            if (_activeDialog is { } dialog)
            {
                dialog.Visibility = Visibility.Collapsed;
                dialog.Hide();
            }
            if (Root.XamlRoot != null)
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).ToArray())
                    popup.IsOpen = false;
            foreach (var popup in _privacyPopups.ToArray()) HidePrivatePopup(popup);
            _native.ClearAlert();
            Notice.IsOpen = false;
            if (Root.IsLoaded) ShowWindow();
        }
        else
        {
            PrivacyCover.Visibility = Visibility.Collapsed;
            PrivateShell.Visibility = Visibility.Visible;
            PrivateShell.IsHitTestVisible = true;
            foreach (var popup in _privacyHiddenPopups.ToArray()) popup.AppWindow.Show(false);
            _privacyHiddenPopups.Clear();
        }
        PositionViews();
        UpdateStatus();
        Save();
        if (Root.IsLoaded) RestorePrivacyFocus();
    }

    private void RestorePrivacyFocus()
    {
        if (_privacyCovered) RestorePrivacyButton.Focus(FocusState.Programmatic);
        else if (!_home) FocusWeb();
        else (_state.FocusView ? FocusPrivacyButton : PrivacyButton).Focus(FocusState.Programmatic);
    }

    internal void RegisterPrivacyPopup(Window window)
    {
        _privacyPopups.Add(window);
        window.Closed += (_, _) => { _privacyPopups.Remove(window); _privacyHiddenPopups.Remove(window); };
        window.Activated += (_, _) => { if (_privacyCovered) { HidePrivatePopup(window); ShowWindow(); RestorePrivacyFocus(); } };
        if (_privacyCovered) HidePrivatePopup(window);
    }

    private void HidePrivatePopup(Window window)
    {
        if (!window.AppWindow.IsVisible) return;
        _privacyHiddenPopups.Add(window);
        window.AppWindow.Hide();
    }

    private void Privacy_Click(object sender, RoutedEventArgs e) => RequestPrivacy();
}
