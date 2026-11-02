using Gather.Core;
using Gather.Signal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System.Text.Json;
using Windows.Storage.Streams;

namespace Gather.App;

internal sealed class SignalServiceSession : IServiceSession
{
    public Account Account { get; }
    public UserControl View { get; } = new();
    FrameworkElement IServiceSession.View => View;
    public void FocusContent() { if (_composer.IsEnabled && _chat.Visibility == Visibility.Visible) _composer.Focus(FocusState.Programmatic); else _linkButton.Focus(FocusState.Programmatic); }
    public string Status { get; private set; } = "Starting Signal…";
    public bool Ready => _connected && _client?.Number != null;
    public UnreadState Unread { get; } = new();
    private readonly MainWindow _host;
    private readonly bool _diagnostic;
    private readonly string _directory;
    private readonly ProtectedHistoryStore _storage;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _link;
    private SignalRuntime? _runtime;
    private SignalAccountClient? _client;
    private SignalHistory _history = new();
    private Task? _initialization;
    private bool _closed, _connected, _rendering, _savingFailed, _sending;
    private bool _composingText;
    private readonly Dictionary<string, SignalConversationItem> _conversationItems = new();
    private readonly SemaphoreSlim _avatarRequests = new(2);
    private readonly DispatcherTimer _avatarTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Grid _root = new() { Padding = new(16), RowSpacing = 10 };
    private readonly StackPanel _setup = new() { Spacing = 14, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _explanation = Label("", 14);
    private readonly TextBlock _stateLabel = Label("", 12);
    private readonly Button _linkButton = new() { Content = "Link Signal", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _cancel = new() { Content = "Cancel linking", Visibility = Visibility.Collapsed };
    private readonly Canvas _qr = new() { Width = 260, Height = 260, Background = new SolidColorBrush(Microsoft.UI.Colors.White), Visibility = Visibility.Collapsed };
    private readonly Grid _chat = new() { RowSpacing = 10, Visibility = Visibility.Collapsed };
    private readonly ComboBox _conversations = new() { PlaceholderText = "Choose a conversation", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _messages = new() { Spacing = 12 };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _composer = new() { PlaceholderText = "Write a message", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 140, MinHeight = 58, MaxLength = 10000 };
    private readonly Button _send = new() { Content = "Send", HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _chatHint = Label("Text preview · New messages only. Files and calls are not available yet.", 11);

    public SignalServiceSession(Account account, MainWindow host, bool diagnostic)
    {
        Account = account; _host = host; _diagnostic = diagnostic;
        if (!Guid.TryParseExact(account.Id, "N", out _)) throw new ArgumentException("Invalid Signal account identifier.");
        _directory = System.IO.Path.Combine(host.AccountDataDirectory, "Signal", account.Id);
        _storage = new(_directory);
        Build();
        View.GotFocus += (_, _) => host.FocusAccount(account.Id);
        if (diagnostic)
        {
            Status = "Sample conversation · offline visual check";
            var sample = _history.Get("sample", false, "Sample conversation");
            sample.Messages.Add(new() { Sender = "Sample contact", Text = "Signal conversations will appear here inside Gather.", Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            sample.Messages.Add(new() { Sender = "You", Text = "This is an offline interface preview.", Outgoing = true, Status = "Sample", Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            _history.SelectedId = sample.Id;
            _setup.Visibility = Visibility.Collapsed; _chat.Visibility = Visibility.Visible;
            _chatHint.Text = "Offline visual check · Sample content · No account connected";
            RefreshConversations(); _send.IsEnabled = false; _composer.IsEnabled = false;
        }
    }
    private static TextBlock Label(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private void Build()
    {
        _root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        _root.Children.Add(_stateLabel);
        var body = new Grid(); Grid.SetRow(body, 1); _root.Children.Add(body);
        var logo = new Image { Width = 40, Height = 40, Stretch = Stretch.Uniform, Source = new BitmapImage(new Uri("ms-appx:///Assets/Services/signal.png")) };
        _setup.Children.Add(new Border { Child = logo, Padding = new(20), Width = 80, Height = 80, HorizontalAlignment = HorizontalAlignment.Left });
        _setup.Children.Add(Label("Signal, here in Gather", 24));
        _setup.Children.Add(Label("Development preview using an unofficial Signal connection. Ordinary text only; files, calls and disappearing messages are not supported yet.", 12));
        _setup.Children.Add(_explanation); _setup.Children.Add(_qr); _setup.Children.Add(_linkButton); _setup.Children.Add(_cancel);
        body.Children.Add(new ScrollViewer { Content = _setup, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            _chat.RowDefinitions.Add(new() { Height = height });
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _conversations.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid ColumnSpacing="10" Padding="0,2">
                    <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                    <PersonPicture Width="32" Height="32" DisplayName="{Binding Name}" ProfilePicture="{Binding Avatar}"/>
                    <TextBlock Grid.Column="1" Text="{Binding Title}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                </Grid>
            </DataTemplate>
            """);
        // Fetch only pictures that are actually shown in the header/dropdown.
        _avatarTimer.Tick += (_, _) =>
        {
            if (_closed) { _avatarTimer.Stop(); return; }
            foreach (var item in _conversations.Items.OfType<SignalConversationItem>())
                if (_conversations.ContainerFromItem(item) is FrameworkElement { IsLoaded: true }) _ = LoadAvatar(item);
        };
        _conversations.DropDownOpened += (_, _) => _avatarTimer.Start();
        _conversations.DropDownClosed += (_, _) => _avatarTimer.Stop();
        header.Children.Add(_conversations);
        var add = new Button { Content = "+", Width = 38 };
        ToolTipService.SetToolTip(add, "New conversation");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(add, "New Signal conversation");
        add.Click += async (_, _) => await NewConversation();
        Grid.SetColumn(add, 1); header.Children.Add(add); _chat.Children.Add(header);
        _scroll.Content = _messages; Grid.SetRow(_scroll, 1); _chat.Children.Add(_scroll);
        var compose = new StackPanel { Spacing = 8 }; compose.Children.Add(_composer); compose.Children.Add(_send);
        Grid.SetRow(compose, 2); _chat.Children.Add(compose);
        Grid.SetRow(_chatHint, 3); _chat.Children.Add(_chatHint);
        body.Children.Add(_chat); View.Content = _root;
        _linkButton.Click += async (_, _) => await BeginLink();
        _cancel.Click += (_, _) => CancelLink();
        _send.Click += async (_, _) => await Send();
        _composer.TextCompositionStarted += (_, _) => _composingText = true;
        _composer.TextCompositionEnded += (_, _) => _composingText = false;
        _composer.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter || _composingText) return;
            bool shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            bool alt = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            if (shift || alt) return;
            e.Handled = true;
            if (!e.KeyStatus.WasKeyDown) await Send();
        };
        ToolTipService.SetToolTip(_send, "Send (Enter)");
        ToolTipService.SetToolTip(_composer, "Enter to send · Shift+Enter for a new line");
        _composer.TextChanged += (_, _) =>
        {
            if (!_rendering && Selected is { } c) { c.Draft = _composer.Text; SaveHistory(); }
        };
        _conversations.SelectionChanged += (_, _) =>
        {
            if (_rendering || _conversations.SelectedItem is not SignalConversationItem item) return;
            var c = item.Conversation;
            _history.SelectedId = c.Id; RenderMessages(); Acknowledge(); SaveHistory();
            _ = LoadAvatar(item);
        };
        ApplyTheme();
    }
    private SignalConversation? Selected => _history.Conversations.FirstOrDefault(c => c.Id == _history.SelectedId);
    public Task InitializeAsync() => _initialization ??= InitializeCore();
    private async Task InitializeCore()
    {
        if (_diagnostic || _closed) return;
        try { _history = _storage.Load(); }
        catch (Exception e)
        {
            _savingFailed = true; _host.Diagnostic("signal-history-load", e);
            SetSetup("Saved Signal messages could not be opened. The original data has been kept. Restart Gather after checking access to this Windows account.", false);
            SetStatus("Saved messages unavailable"); return;
        }
        try
        {
            SetStatus("Checking Signal component…");
            await StartRuntime(_lifetime.Token);
            if (await _client!.RestoreAsync(_lifetime.Token)) await ShowConnected();
            else { SetSetup("Open Signal on your phone, then Settings → Linked devices. Choose Link Signal here to display the QR code.", true); SetStatus("Ready to link your phone"); }
        }
        catch (FileNotFoundException) when (_runtime is null)
        {
            if (_closed) return;
            SetSetup("The Signal connection component is missing or incomplete. Install the verified component, then choose Reconnect from the pane menu to link your phone.", false);
            SetStatus("Signal component needed");
        }
        catch (Exception e) { StopRuntime(); if (!_closed) ConnectionFailure(e); }
    }
    private async Task StartRuntime(CancellationToken cancellationToken)
    {
        var runtime = await SignalRuntime.StartAsync(AppContext.BaseDirectory, System.IO.Path.Combine(_directory, "connection"), cancellationToken);
        if (_closed || cancellationToken.IsCancellationRequested) { runtime.Dispose(); throw new OperationCanceledException(cancellationToken); }
        _runtime = runtime;
        _client = new(_runtime.Connection);
        _runtime.Connection.Notification += notification => View.DispatcherQueue.TryEnqueue(() => { if (ReferenceEquals(_runtime, runtime)) Receive(notification); });
        _runtime.Connection.Disconnected += () => View.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed && ReferenceEquals(_runtime, runtime) && _link is null) { _connected = false; SetStatus("Signal disconnected · Choose Reconnect in the pane menu"); _send.IsEnabled = false; }
        });
        _runtime.Connection.Start();
    }
    private async Task BeginLink()
    {
        if (_closed || _link is not null || _diagnostic || _savingFailed) return;
        var linking = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        linking.CancelAfter(TimeSpan.FromMinutes(2)); _link = linking;
        _linkButton.IsEnabled = false; _cancel.Visibility = Visibility.Visible;
        try
        {
            if (_runtime is null) await StartRuntime(linking.Token);
            SetStatus("Preparing your link code…");
            string uri = await _client!.StartLinkAsync(linking.Token);
            if (_closed || linking.IsCancellationRequested) return;
            DrawQr(uri);
            _explanation.Text = "In Signal on your phone, open Settings → Linked devices → Link a new device. Scan this code. It expires after two minutes.";
            SetStatus("Waiting for approval on your phone");
            await _client.FinishLinkAsync(uri, linking.Token);
            ClearQr();
            await ShowConnected();
        }
        catch (OperationCanceledException)
        {
            if (!_closed) { StopRuntime(); SetSetup("Linking ended. Choose Link Signal for a fresh code.", true); SetStatus("Not linked"); }
        }
        catch (Exception e) { if (!_closed) { StopRuntime(); ConnectionFailure(e); SetSetup("Signal could not complete linking. Try a fresh code, or reconnect from the pane menu.", true); } }
        finally
        {
            ClearQr(); _cancel.Visibility = Visibility.Collapsed;
            if (ReferenceEquals(_link, linking)) _link = null;
            linking.Dispose(); if (!_closed) _linkButton.IsEnabled = !_savingFailed;
        }
    }
    private void CancelLink() { _link?.Cancel(); ClearQr(); }
    private void DrawQr(string uri)
    {
        ClearQr(); var modules = LinkQrCode.Encode(uri);
        for (int y = 0; y < LinkQrCode.Size; y++) for (int x = 0; x < LinkQrCode.Size; x++)
        {
            if (!modules[x, y]) continue;
            var square = new Rectangle { Width = 4, Height = 4, Fill = new SolidColorBrush(Microsoft.UI.Colors.Black) };
            Canvas.SetLeft(square, (x + 4) * 4); Canvas.SetTop(square, (y + 4) * 4); _qr.Children.Add(square);
        }
        _qr.Visibility = Visibility.Visible;
    }
    private void ClearQr() { _qr.Children.Clear(); _qr.Visibility = Visibility.Collapsed; }
    private async Task ShowConnected()
    {
        if (_closed) return;
        // Subscribe only after the receive handler and active account have been installed.
        _history.UpdateContacts(await _client!.ContactsAsync(_lifetime.Token));
        _history.UpdateGroups(await _client.GroupsAsync(_lifetime.Token));
        await _client.SubscribeAsync(_lifetime.Token);
        if (_closed) return;
        _connected = true; _setup.Visibility = Visibility.Collapsed; _chat.Visibility = Visibility.Visible;
        SetStatus("Signal linked · Text development preview"); RefreshConversations(); SaveHistory();
    }
    private void Receive(JsonElement notification)
    {
        if (_closed || _savingFailed || _client?.Number is null) return;
        try
        {
            var received = _history.Apply(notification, _client.Number);
            if (_host.IsAccountActive(Account.Id) && Selected is { } selected)
                foreach (var m in selected.Messages) m.Unread = false;
            if (!SaveHistory()) return;
            RefreshConversations();
            if (received is { } item && item.Message.Unread)
                _host.ShowServiceNotification(Account, item.Conversation.Name, item.Message.Text);
        }
        catch (Exception e) { ConnectionFailure(e); StopRuntime(); }
    }
    private void RefreshConversations()
    {
        _rendering = true;
        var entries = _history.Conversations.Where(c => !c.Blocked).OrderByDescending(c => c.Messages.LastOrDefault()?.Timestamp ?? 0).ThenBy(c => c.Name).ToArray();
        var items = entries.Select(c =>
        {
            if (!_conversationItems.TryGetValue(c.Id, out var item)) _conversationItems[c.Id] = item = new(c);
            item.RefreshLabels(); return item;
        }).ToArray();
        _conversations.ItemsSource = items;
        var selected = entries.FirstOrDefault(c => c.Id == _history.SelectedId) ?? entries.FirstOrDefault();
        _history.SelectedId = selected?.Id;
        _conversations.SelectedItem = items.FirstOrDefault(item => item.Conversation == selected);
        _rendering = false; RenderMessages(); UpdateUnread();
        if (_conversations.SelectedItem is SignalConversationItem current) _ = LoadAvatar(current);
    }
    private async Task LoadAvatar(SignalConversationItem item)
    {
        if (_diagnostic || _closed || !Ready || item.Conversation.Blocked || item.Loading || item.RefreshAfter > DateTimeOffset.UtcNow) return;
        var client = _client!;
        item.Loading = true;
        bool entered = false;
        try
        {
            await _avatarRequests.WaitAsync(_lifetime.Token); entered = true;
            if (_closed || item.Conversation.Blocked || !ReferenceEquals(client, _client)) return;
            byte[]? data = await client.AvatarAsync(item.Conversation.Recipient, item.Conversation.Group, _lifetime.Token);
            BitmapImage? picture = data is null ? null : await DecodeAvatarImage(data);
            if (!_closed && !item.Conversation.Blocked && ReferenceEquals(client, _client))
            {
                item.SetAvatar(picture);
                item.RefreshAfter = DateTimeOffset.UtcNow.Add(picture is null ? TimeSpan.FromMinutes(2) : TimeSpan.FromHours(1));
            }
        }
        catch (OperationCanceledException) { item.RefreshAfter = DateTimeOffset.UtcNow.AddMinutes(2); }
        catch (Exception e) when (e is IOException or ArgumentException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        { item.RefreshAfter = DateTimeOffset.UtcNow.AddMinutes(2); } // Keep chat usable when a picture cannot load.
        finally { item.Loading = false; if (entered) _avatarRequests.Release(); }
    }
    private static async Task<BitmapImage?> DecodeAvatarImage(byte[] data)
    {
        if (data.Length is 0 or > SignalAvatar.MaximumBytes) return null;
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(data); await writer.StoreAsync(); }
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        if (decoder.PixelWidth is 0 or > 4096 || decoder.PixelHeight is 0 or > 4096) return null;
        stream.Seek(0);
        var picture = new BitmapImage { DecodePixelWidth = 96, DecodePixelHeight = 96 };
        await picture.SetSourceAsync(stream);
        return picture;
    }
    internal async Task<bool> ShowDiagnosticAvatar()
    {
        if (!_diagnostic || _conversations.SelectedItem is not SignalConversationItem item) return false;
        // Original synthetic silhouette, never a user's profile picture.
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAaUlEQVR4nO3VsREAEAyFYWsZyXS2czpqConEiZxXpPV/BRJiys1yAgAAuAWU2oa5BpjDWsgWgIpLEAAAAICvf2AFkZ7jdxf8DTh1AbcBZs+QG5ZASIA0zkUsAdo4B/Eu4FScQgAAgDmgA4vpHQZYCoDvAAAAAElFTkSuQmCC");
        item.SetAvatar(await DecodeAvatarImage(png));
        return item.Avatar is not null && await DecodeAvatarImage(new byte[SignalAvatar.MaximumBytes + 1]) is null;
    }
    private void RenderMessages()
    {
        _rendering = true; _messages.Children.Clear();
        var selected = Selected;
        if (selected is null) _messages.Children.Add(Label("Choose a conversation or use + to start one.", 14));
        else if (selected.Messages.Count == 0) _messages.Children.Add(Label("New messages will appear here. This preview does not import older phone history.", 14));
        if (selected is not null)
            foreach (var message in selected.Messages.OrderBy(m => m.Timestamp).TakeLast(150))
            {
                var stack = new StackPanel { Spacing = 4 };
                stack.Children.Add(Label(message.Sender, 11));
                var body = Label(message.Text, 14 * Account.Zoom); body.IsTextSelectionEnabled = true; stack.Children.Add(body);
                stack.Children.Add(Label(message.Status, 10));
                var bubble = new Border { Child = stack, Padding = new(12), CornerRadius = new(10), MaxWidth = 520,
                    HorizontalAlignment = message.Outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    Background = new SolidColorBrush(message.Outgoing
                        ? (_host.IsDark ? Windows.UI.Color.FromArgb(255, 36, 64, 66) : Windows.UI.Color.FromArgb(255, 226, 241, 238))
                        : (_host.IsDark ? Windows.UI.Color.FromArgb(255, 43, 48, 57) : Windows.UI.Color.FromArgb(255, 240, 243, 247))) };
                _messages.Children.Add(bubble);
            }
        _composer.Text = selected?.Draft ?? "";
        _composer.IsEnabled = !_diagnostic && selected is { Blocked: false, ExpirationSeconds: 0 } && !_savingFailed;
        _send.IsEnabled = _composer.IsEnabled && Ready && !_sending;
        if (selected?.ExpirationSeconds > 0) _chatHint.Text = "Disappearing messages are not supported in this text preview. Sending is disabled for this conversation.";
        else if (!_diagnostic) _chatHint.Text = "Enter to send · Shift+Enter for a new line · Text preview";
        _rendering = false;
    }
    private async Task NewConversation()
    {
        if (_diagnostic || !Ready) return;
        var input = new TextBox { Header = "Signal phone number", PlaceholderText = "+32…", MaxLength = 20 };
        var dialog = new ContentDialog { XamlRoot = View.XamlRoot, Title = "New Signal conversation", Content = input,
            PrimaryButtonText = "Open conversation", CloseButtonText = "Cancel", RequestedTheme = View.RequestedTheme };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(input.Text.Trim(), @"^\+[1-9]\d{5,14}$"))
            { input.Header = "Use a full number with its country code, beginning with +"; e.Cancel = true; }
        };
        if (await _host.ShowSignalDialog(dialog) != ContentDialogResult.Primary || _closed) return;
        _history.SelectedId = _history.Get(input.Text.Trim(), false).Id; SaveHistory(); RefreshConversations();
    }
    private async Task Send()
    {
        var chat = Selected; string text = _composer.Text;
        if (!Ready || _diagnostic || _sending || _savingFailed || chat is null || chat.Blocked || chat.ExpirationSeconds > 0 || string.IsNullOrWhiteSpace(text)) return;
        var pending = new SignalMessage { Sender = "You", Text = text, Outgoing = true, Status = "Sending", Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        chat.Messages.Add(pending); chat.Draft = ""; _sending = true;
        if (!SaveHistory()) { chat.Messages.Remove(pending); chat.Draft = text; _sending = false; RenderMessages(); return; }
        RenderMessages();
        try
        {
            long timestamp = await _client!.SendAsync(chat.Recipient, chat.Group, text, _lifetime.Token);
            pending.Timestamp = timestamp; pending.Id = "self:" + timestamp; pending.Status = "Sent";
            chat.Messages.RemoveAll(m => !ReferenceEquals(m, pending) && m.Outgoing && m.Timestamp == timestamp);
        }
        catch (SignalRpcException e)
        {
            pending.Status = "Not confirmed — check Signal before trying again";
            _host.Diagnostic("signal-send", e);
            SetStatus("Signal could not send. Check the connection and contact’s identity in Signal.");
        }
        catch (Exception e)
        {
            pending.Status = "Delivery unknown — check before sending again";
            if (!_closed) _host.Diagnostic("signal-send", e);
        }
        finally { _sending = false; if (!_closed) { SaveHistory(); RefreshConversations(); } }
    }
    private bool SaveHistory()
    {
        if (_diagnostic) return true;
        if (_savingFailed || _closed) return false;
        try { _storage.Save(_history); return true; }
        catch (Exception e)
        {
            _savingFailed = true; _host.Diagnostic("signal-history-save", e);
            SetStatus("Signal paused · Messages could not be saved"); _send.IsEnabled = false; StopRuntime(); return false;
        }
    }
    private void SetSetup(string text, bool link)
    {
        _setup.Visibility = Visibility.Visible; _chat.Visibility = Visibility.Collapsed;
        _explanation.Text = text; _linkButton.IsEnabled = link; ClearQr();
    }
    private void SetStatus(string status) { Status = status; _stateLabel.Text = status; _host.UpdateStatus(); }
    private void ConnectionFailure(Exception error)
    {
        _connected = false; _host.Diagnostic("signal-connection", error);
        SetStatus("Could not connect Signal · Choose Reconnect in the pane menu"); _send.IsEnabled = false;
        if (_chat.Visibility != Visibility.Visible) SetSetup("Signal could not start. Your saved connection and messages have been kept. Try Reconnect from the pane menu.", false);
    }
    private void StopRuntime() { var runtime = _runtime; _runtime = null; _connected = false; runtime?.Dispose(); }
    public void Acknowledge()
    {
        if (!_host.IsAccountActive(Account.Id) || Selected is not { } selected) return;
        bool changed = selected.Messages.Any(m => m.Unread);
        foreach (var message in selected.Messages) message.Unread = false;
        if (changed) { SaveHistory(); RefreshConversations(); }
        UpdateUnread();
    }
    private void UpdateUnread() { Unread.SetCount(_history.Conversations.Where(c => !c.Blocked).Sum(c => c.UnreadCount)); _host.UpdateStatus(); }
    public Task SetContentSize(double zoom) { Account.Zoom = Math.Clamp(zoom, .75, 1.5); RenderMessages(); return Task.CompletedTask; }
    public void SetAudioMuted(bool muted) { } // No native audio playback; notifications use the host policy.
    public void ApplyTheme()
    {
        View.RequestedTheme = _host.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        _root.Background = new SolidColorBrush(_host.IsDark ? Windows.UI.Color.FromArgb(255, 24, 28, 32) : Windows.UI.Color.FromArgb(255, 250, 251, 253));
        RenderMessages();
    }
    public Task ClearDataAsync()
    {
        Close();
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(_host.AccountDataDirectory, "Signal")) + System.IO.Path.DirectorySeparatorChar;
        string target = System.IO.Path.GetFullPath(_directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileName(target) != Account.Id)
            throw new IOException("Invalid Signal data path.");
        if (Directory.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("Signal data folder must not be a link.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(target, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) throw new IOException("Signal data contains a link; automatic deletion stopped.");
            Directory.Delete(target, recursive: true);
        }
        return Task.CompletedTask;
    }
    public void Close()
    {
        if (_closed) return;
        SaveHistory(); _closed = true; _avatarTimer.Stop(); _link?.Cancel(); _lifetime.Cancel(); ClearQr(); StopRuntime();
        foreach (var item in _conversationItems.Values) item.SetAvatar(null);
        _conversationItems.Clear();
    }
}
