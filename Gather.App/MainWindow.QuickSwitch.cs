using Gather.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Gather.App;

public sealed partial class MainWindow
{
    private bool _quickSwitchOpen;
    private Action<VirtualKey>? _quickSwitchDiagnosticKey;

    // The thread-local keyboard hook also reaches this from WebView2 child HWNDs.
    // Reserve the request before queuing so held keys cannot queue several dialogs.
    private bool RequestQuickSwitch()
    {
        if (_privacyCovered) return true;
        if (_quitting) return false;
        if (_quickSwitchOpen) return true;
        if (_dialogs.CurrentCount == 0) return false;
        _quickSwitchOpen = true;
        if (DispatcherQueue.TryEnqueue(() => _ = ShowQuickSwitch())) return true;
        _quickSwitchOpen = false;
        return false;
    }

    private void QuickSwitch_Click(object sender, RoutedEventArgs e) => RequestQuickSwitch();

    private async Task ShowQuickSwitch()
    {
        var previousFocus = FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
        string? selectedId = null;
        try
        {
            if (_dialogs.CurrentCount == 0 || _quitting || _privacyCovered) return;
            var search = new TextBox { Name = "QuickSwitchSearch", PlaceholderText = "Account or provider name", MaxLength = 200 };
            AutomationProperties.SetName(search, "Search service accounts");
            var results = new ListView
            {
                Name = "QuickSwitchResults", SelectionMode = ListViewSelectionMode.Single,
                IsItemClickEnabled = true, MaxHeight = 240, HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(results, "Matching service accounts");
            var summary = new TextBlock { Name = "QuickSwitchSummary", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
            var content = new StackPanel { Spacing = 10, MinWidth = 260, MaxWidth = 460 };
            content.Children.Add(search);
            content.Children.Add(summary);
            content.Children.Add(results);
            content.Children.Add(Text("Up/Down to select · Enter to switch · Escape to close", 12));
            var dialog = Dialog("Switch service", content, "Switch", "Cancel");
            dialog.DefaultButton = ContentDialogButton.None;
            AutomationProperties.SetHelpText(search, "Searches account and provider names only. Empty search lists recent accounts first. Use Up and Down to select, Enter to switch, or Escape to close.");
            IReadOnlyList<Account> matches = [];
            void Announce()
            {
                var selected = results.SelectedIndex >= 0 && results.SelectedIndex < matches.Count ? matches[results.SelectedIndex] : null;
                summary.Text = selected == null
                    ? _state.Accounts.Count == 0 ? "No service accounts yet. Add a service to get started." : "No matching accounts. Try another name."
                    : $"{(string.IsNullOrWhiteSpace(search.Text) ? "Recent accounts" : "Matching accounts")}: {results.SelectedIndex + 1} of {matches.Count}. {selected.Name}, {Providers.Find(selected.ProviderId)!.Name}.";
                FrameworkElementAutomationPeer.CreatePeerForElement(summary)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
            void Filter()
            {
                // Disconnect the source while rebuilding, before selection events fire.
                results.Items.Clear();
                matches = QuickSwitch.Search(_state, search.Text);
                foreach (var account in matches)
                {
                    string provider = Providers.Find(account.ProviderId)!.Name;
                    var row = new StackPanel { Spacing = 2 };
                    row.Children.Add(new TextBlock { Text = account.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                    row.Children.Add(new TextBlock { Text = provider, FontSize = 12, Opacity = .7 });
                    var item = new ListViewItem { Content = row, Tag = account.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    AutomationProperties.SetName(item, account.Name + ", " + provider);
                    ToolTipService.SetToolTip(item, account.Name + " · " + provider);
                    results.Items.Add(item);
                }
                results.SelectedIndex = matches.Count > 0 ? 0 : -1;
                dialog.IsPrimaryButtonEnabled = matches.Count > 0;
                Announce();
            }
            void Choose()
            {
                if (results.SelectedIndex < 0 || results.SelectedIndex >= matches.Count) return;
                selectedId = matches[results.SelectedIndex].Id;
                dialog.Hide();
            }
            bool HandleCommand(VirtualKey key)
            {
                if (key is VirtualKey.Up or VirtualKey.Down)
                {
                    if (matches.Count > 0)
                    {
                        int next = Math.Clamp(results.SelectedIndex + (key == VirtualKey.Down ? 1 : -1), 0, matches.Count - 1);
                        results.SelectedIndex = next;
                        results.ScrollIntoView(results.SelectedItem);
                    }
                    return true;
                }
                if (key == VirtualKey.Enter) { Choose(); return true; }
                if (key == VirtualKey.Escape) { dialog.Hide(); return true; }
                return false;
            }
            void HandleKey(object sender, KeyRoutedEventArgs e) => e.Handled = HandleCommand(e.Key);
            if (_smoke && System.Environment.GetCommandLineArgs().Contains("--smoke-quick-switch"))
                _quickSwitchDiagnosticKey = key => HandleCommand(key);
            search.TextChanged += (_, _) => Filter();
            search.PreviewKeyDown += HandleKey;
            results.PreviewKeyDown += HandleKey;
            results.SelectionChanged += (_, _) => Announce();
            results.ItemClick += (_, e) =>
            {
                selectedId = (e.ClickedItem as ListViewItem)?.Tag as string;
                if (selectedId != null) dialog.Hide();
            };
            dialog.PrimaryButtonClick += (_, e) =>
            {
                if (results.SelectedIndex < 0) { e.Cancel = true; return; }
                selectedId = matches[results.SelectedIndex].Id;
            };
            dialog.Opened += (_, _) => search.Focus(FocusState.Programmatic);
            Filter();
            await ShowDialog(dialog);
        }
        catch (Exception ex)
        {
            Diagnostic("quick-switch", ex);
            ShowNotice("Quick switch unavailable", "Use the service sidebar and try Ctrl+K again.", InfoBarSeverity.Warning);
        }
        finally { _quickSwitchOpen = false; _quickSwitchDiagnosticKey = null; }
        if (_quitting || _privacyCovered) return;
        if (selectedId != null) SelectAccount(selectedId);
        else if (previousFocus?.Focus(FocusState.Programmatic) != true)
        {
            if (_home) QuickSwitchButton.Focus(FocusState.Programmatic);
            else FocusWeb();
        }
    }
}
