using Gather.Signal;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;

namespace Gather.App;

// Image sources stay in this service session, outside persisted message history.
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class SignalConversationItem(SignalConversation conversation) : INotifyPropertyChanged
{
    public SignalConversation Conversation { get; } = conversation;
    public string Name => Conversation.Name;
    public string Title => Conversation.ToString();
    public ImageSource? Avatar { get; private set; }
    public DateTimeOffset RefreshAfter { get; set; }
    public bool Loading { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshLabels()
    {
        PropertyChanged?.Invoke(this, new(nameof(Name)));
        PropertyChanged?.Invoke(this, new(nameof(Title)));
    }
    public void SetAvatar(ImageSource? avatar)
    {
        Avatar = avatar;
        PropertyChanged?.Invoke(this, new(nameof(Avatar)));
    }
}
