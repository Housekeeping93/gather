using Gather.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace Gather.App;

internal interface IServiceSession
{
    Account Account { get; }
    FrameworkElement View { get; }
    string Status { get; }
    bool Ready { get; }
    UnreadState Unread { get; }
    Task InitializeAsync();
    Task ClearDataAsync();
    Task SetContentSize(double zoom);
    void ApplyTheme();
    // Apply effective audio policy without changing saved account preferences.
    void SetAudioMuted(bool muted);
    void Acknowledge();
    void FocusContent();
    void Close();
}
