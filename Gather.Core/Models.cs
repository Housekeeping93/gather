using System.Text.Json;
namespace Gather.Core;

public enum ProviderKind { Web, Signal }
public sealed record Provider(string Id, string Name, string Glyph, string Color, string Url, string Description, string[] Hosts, ProviderKind Kind = ProviderKind.Web);
public static class Providers
{
    public static readonly Provider[] All = [
        new("whatsapp", "WhatsApp", "W", "#20A675", "https://web.whatsapp.com/", "Link from your phone with a QR code.", ["web.whatsapp.com", "www.whatsapp.com"]),
        new("telegram", "Telegram", "T", "#3899D8", "https://web.telegram.org/a/", "Approve the connection in Telegram.", ["web.telegram.org", "oauth.telegram.org"]),
        new("slack", "Slack", "S", "#8B5BA5", "https://slack.com/signin", "Sign in to your workspace. Email code is recommended.", ["slack.com"]),
        new("messenger", "Messenger", "M", "#5274E8", "https://www.facebook.com/messages/", "Facebook messaging. Experimental support.", ["www.facebook.com", "www.messenger.com", "messenger.com"]),
        new("discord", "Discord", "D", "#6670DC", "https://discord.com/app", "Your servers and direct messages. Experimental support.", ["discord.com"]),
        new("signal", "Signal", "S", "#3B45FD", "", "Link your phone. Conversations stay inside Gather. Development preview.", [], ProviderKind.Signal)
    ];
    public static Provider? Find(string id) => All.FirstOrDefault(x => x.Id == id);
}
public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProviderId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? StartUrl
    {
        get; set;
    }
    public bool StartConnected { get; set; } = true;
    public bool Muted
    {
        get; set;
    }
    public double Zoom { get; set; } = 1;
    public string ProfileName => "account-" + Id;
}
public sealed class Layout
{
    public string? Left
    {
        get; set;
    }
    public string? Right
    {
        get; set;
    }
    public bool Split
    {
        get; set;
    }
    public bool FocusRight
    {
        get; set;
    }
    public double Ratio { get; set; } = .5;
}
public sealed class Settings
{
    public int Version { get; set; } = 1;
    public List<Account> Accounts { get; set; } = [];
    public List<string> RecentAccountIds { get; set; } = [];
    public Layout Layout { get; set; } = new();
    public QuietSettings Quiet { get; set; } = new();
    public string Theme { get; set; } = "Light";
    public string ShellDensity { get; set; } = "Compact";
    public bool FocusView { get; set; }
    public string PrivacyShortcut { get; set; } = "Ctrl+Shift+P";
    public bool PrivacyCovered { get; set; }
    public string TaskbarBadge { get; set; } = "Number";
    public bool CloseToTray { get; set; } = true;
    public bool HidePreviews { get; set; } = true;
    public int WindowWidth { get; set; } = 1360;
    public int WindowHeight { get; set; } = 900;
}

public static class LayoutActions
{
    public static bool Navigate(Layout layout, IReadOnlyList<string> order, int direction)
    {
        if (order.Count < 2 || direction == 0)
            return false;
        bool forward = direction > 0;
        string? anchor = layout.Split && forward ? layout.Right : layout.Left;
        int index = Array.IndexOf(order.ToArray(), anchor);
        if (index < 0)
            index = 0;
        string next = order[(index + (forward ? 1 : -1) + order.Count) % order.Count];
        if (!layout.Split)
            layout.Left = next;
        else if (forward)
        {
            layout.Left = order[index];
            layout.Right = next;
        }
        else
        {
            layout.Right = order[index];
            layout.Left = next;
        }
        layout.FocusRight = layout.Split && forward;
        return true;
    }
    public static bool MoveAccount(Settings state, string id, int destination)
    {
        int index = state.Accounts.FindIndex(a => a.Id == id);
        if (index < 0 || state.Accounts.Count < 2)
            return false;
        destination = Math.Clamp(destination, 0, state.Accounts.Count - 1);
        if (index == destination)
            return false;
        var account = state.Accounts[index];
        state.Accounts.RemoveAt(index);
        state.Accounts.Insert(destination, account);
        return true;
    }
    public static void Select(Layout layout, string id, bool? right = null)
    {
        if (layout.Left == id)
        {
            layout.FocusRight = false;
            return;
        }
        if (layout.Split && layout.Right == id)
        {
            layout.FocusRight = true;
            return;
        }
        bool targetRight = layout.Split && (right ?? layout.FocusRight);
        if (targetRight)
            layout.Right = id;
        else
        {
            layout.Left = id;
            if (layout.Right == id)
                layout.Right = null;
        }
        layout.FocusRight = targetRight;
    }
    public static bool EnableSplit(Layout layout, IEnumerable<string> accounts)
    {
        var ids = accounts.ToArray();
        layout.Left ??= ids.FirstOrDefault();
        layout.Right = ids.Contains(layout.Right) && layout.Right != layout.Left ? layout.Right : ids.FirstOrDefault(x => x != layout.Left);
        layout.Split = layout.Left != null && layout.Right != null;
        return layout.Split;
    }
    public static void Collapse(Layout layout)
    {
        if (layout.FocusRight && layout.Right != null)
            (layout.Left, layout.Right) = (layout.Right, layout.Left);
        layout.Split = false;
        layout.FocusRight = false;
    }
    public static void Swap(Layout layout)
    {
        if (!layout.Split)
            return;
        (layout.Left, layout.Right) = (layout.Right, layout.Left);
        layout.FocusRight = !layout.FocusRight;
        layout.Ratio = 1 - layout.Ratio;
    }
    public static void Normalize(Settings state)
    {
        state.WindowWidth = Math.Clamp(state.WindowWidth, 960, 3840);
        state.WindowHeight = Math.Clamp(state.WindowHeight, 640, 2160);
        if (state.Theme is not ("Light" or "Dark" or "System"))
            state.Theme = "System";
        if (state.TaskbarBadge is not ("Number" or "Dot" or "Off"))
            state.TaskbarBadge = "Number";
        state.Accounts ??= [];
        state.Accounts = state.Accounts.Where(a => a != null && Guid.TryParseExact(a.Id, "N", out _) && Providers.Find(a.ProviderId) != null).DistinctBy(a => a.Id).ToList();
        foreach (var a in state.Accounts)
        {
            a.Zoom = double.IsFinite(a.Zoom) ? Math.Clamp(a.Zoom, .5, 2) : 1;
            if (string.IsNullOrWhiteSpace(a.Name))
                a.Name = Providers.Find(a.ProviderId)!.Name;
        }
        state.Quiet ??= new();
        ShellAppearance.Normalize(state);
        PrivacyCurtain.Normalize(state);
        QuickSwitch.Normalize(state);
        QuietPolicy.Normalize(state.Quiet, state.Accounts);
        state.Layout ??= new();
        var l = state.Layout;
        var ids = state.Accounts.Select(a => a.Id).ToArray();
        if (!ids.Contains(l.Left))
            l.Left = ids.FirstOrDefault();
        if (!ids.Contains(l.Right) || l.Right == l.Left)
            l.Right = ids.FirstOrDefault(x => x != l.Left);
        l.Split &= l.Left != null && l.Right != null;
        l.FocusRight &= l.Split;
        l.Ratio = double.IsFinite(l.Ratio) ? Math.Clamp(l.Ratio, .3, .7) : .5;
    }
}

public sealed class SettingsStore(string directory, string fileName = "settings.json")
{
    private bool _preserveUnreadable;
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string? LoadWarning
    {
        get; private set;
    }
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public Settings Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path) && fileName != "settings.json")
            path = Path.Combine(DirectoryPath, "settings.json");
        if (!File.Exists(path))
            return new();
        try
        {
            var state = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new();
            LayoutActions.Normalize(state);
            return state;
        }
        catch (JsonException)
        {
            try
            {
                File.Copy(path, path + ".unreadable-" + Guid.NewGuid().ToString("N"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _preserveUnreadable = true; }
            try
            {
                var backup = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path + ".bak"));
                if (backup != null)
                {
                    LayoutActions.Normalize(backup);
                    LoadWarning = "Recovered settings from the previous saved copy. Your browser sessions have been kept.";
                    return backup;
                }
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
            LoadWarning = "Settings could not be read. The original settings and browser profiles have been kept.";
            return new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _preserveUnreadable = true;
            LoadWarning = "Settings could not be read. Restart Gather after checking folder access; existing settings will not be overwritten.";
            return new();
        }
    }
    public void Save(Settings state)
    {
        if (_preserveUnreadable)
            throw new IOException("Existing unreadable settings must be preserved.");
        LayoutActions.Normalize(state);
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, fileName), tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, Options));
        if (File.Exists(path))
            File.Replace(tmp, path, path + ".bak");
        else
            File.Move(tmp, path);
    }
}
