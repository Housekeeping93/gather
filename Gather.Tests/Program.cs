using Gather.Core;
if (args.Length == 1 && args[0] == "--signal-process-fixture") { Thread.Sleep(Timeout.Infinite); return; }
if (args.Length == 3 && args[0] == "--signal-runtime-check") { await RuntimeIntegrationChecks.Run(args[1], args[2]); return; }
var passed = 0;
void Check(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); passed++; } catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e.Message); Environment.Exit(1); } }
void Expect(bool value) { if (!value) throw new Exception("Assertion failed"); }
var slack = Providers.Find("slack")!;
Check("Navigation rejects executable URLs, userinfo, custom ports and plaintext", () =>
{
    foreach (var url in new[] { "javascript:alert(1)", "file:///C:/secret.txt", "http://app.slack.com", "https://app.slack.com:444", "https://attacker@app.slack.com", "slack://open" })
        Expect(NavigationPolicy.Decide(slack, url) == NavigationDecision.Block);
});
Check("Navigation does not trust lookalike domains", () =>
{
    foreach (var url in new[] { "https://slack.com.evil.test", "https://evilslack.com", "https://workspace.slack.com.evil.test" })
        Expect(NavigationPolicy.Decide(slack, url) == NavigationDecision.External);
    Expect(NavigationPolicy.Decide(slack, "https://example.slack.com/client") == NavigationDecision.Internal);
    Expect(NavigationPolicy.Decide(slack, "https://accounts.google.com/o/oauth2/auth") == NavigationDecision.IdentityBrowser);
});
Check("Split view focuses existing account without creating duplicate placement", () =>
{
    var l = new Layout { Left = "a" };
    Expect(LayoutActions.EnableSplit(l, ["a", "b"]));
    LayoutActions.Select(l, "a", true);
    Expect(l.Left == "a" && l.Right == "b" && !l.FocusRight);
    LayoutActions.Select(l, "b", false);
    Expect(l.Left == "a" && l.Right == "b" && l.FocusRight);
});
Check("Closing split keeps focused account and remembers its companion", () =>
{
    var l = new Layout { Left = "a", Right = "b", Split = true, FocusRight = true, Ratio = .4 };
    LayoutActions.Collapse(l);
    Expect(l.Left == "b" && l.Right == "a" && !l.Split && !l.FocusRight);
    LayoutActions.EnableSplit(l, ["a", "b"]);
    Expect(l.Left == "b" && l.Right == "a");
});
Check("Swapping preserves focused account and pane sizes", () =>
{
    var l = new Layout { Left = "a", Right = "b", Split = true, FocusRight = false, Ratio = .35 };
    LayoutActions.Swap(l);
    Expect(l.Left == "b" && l.Right == "a" && l.FocusRight && Math.Abs(l.Ratio - .65) < .001);
});
Check("Removing an account repairs stale layout without sharing profiles", () =>
{
    var a = new Account { ProviderId = "whatsapp" };
    var b = new Account { ProviderId = "telegram" };
    var s = new Settings { Accounts = [a, b], Layout = new Layout { Left = a.Id, Right = b.Id, Split = true, FocusRight = true } };
    Expect(a.ProfileName != b.ProfileName);
    s.Accounts.Remove(b);
    LayoutActions.Normalize(s);
    Expect(s.Layout.Left == a.Id && !s.Layout.Split && !s.Layout.FocusRight);
});
Check("Corrupt settings cannot create arbitrary profile paths or unsupported providers", () =>
{
    var s = new Settings { Accounts = [new() { Id = "../../escape", ProviderId = "slack" }, new() { ProviderId = "unknown" }, new() { ProviderId = "slack" }], Layout = new() { Ratio = 99 } };
    LayoutActions.Normalize(s);
    Expect(s.Accounts.Count == 1 && s.Layout.Ratio == .7);
});
Check("Account URL never trusts a persisted external or executable destination", () =>
{
    var a = new Account { ProviderId = "slack", StartUrl = "javascript:alert(1)" };
    Expect(NavigationPolicy.StartUrl(a) == slack.Url);
    a.StartUrl = "https://my-work.slack.com";
    Expect(NavigationPolicy.StartUrl(a) == a.StartUrl);
});
Check("Settings survive save/reload with isolated account and layout IDs", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "Gather-tests-" + Guid.NewGuid().ToString("N"));
    var store = new SettingsStore(dir);
    var a = new Account { ProviderId = "slack", Name = "Work" };
    var b = new Account { ProviderId = "telegram", Name = "Personal" };
    store.Save(new()
    {
        Accounts = [a, b],
        Layout = new()
        {
            Left = a.Id,
            Right = b.Id,
            Split = true,
            Ratio = .6
        }
    });
    var read = store.Load();
    Expect(read.Accounts[0].ProfileName == a.ProfileName && read.Layout.Split && read.Layout.Right == b.Id);
    File.Delete(Path.Combine(dir, "settings.json"));
    Directory.Delete(dir);
});
Check("Badges handle capped counts without inventing unread messages", () =>
{
    Expect(NavigationPolicy.UnreadCount("(12) WhatsApp") == 12);
    Expect(NavigationPolicy.UnreadCount("Telegram") == null);
    Expect(NavigationPolicy.UnreadCount("Slack (work)") == null);
});
Check("Single navigation follows tile order and wraps in both directions", () =>
{
    var l = new Layout { Left = "telegram" };
    string[] order = ["telegram", "whatsapp", "slack"];
    LayoutActions.Navigate(l, order, 1);
    Expect(l.Left == "whatsapp");
    LayoutActions.Navigate(l, order, 1);
    Expect(l.Left == "slack");
    LayoutActions.Navigate(l, order, 1);
    Expect(l.Left == "telegram");
    LayoutActions.Navigate(l, order, -1);
    Expect(l.Left == "slack");
    Expect(!LayoutActions.Navigate(l, [], 1));
    Expect(!LayoutActions.Navigate(l, ["slack"], -1));
});
Check("Split navigation slides the visible pair as specified and reverses", () =>
{
    var l = new Layout { Left = "telegram", Right = "whatsapp", Split = true, Ratio = .4 };
    string[] order = ["telegram", "whatsapp", "slack"];
    LayoutActions.Navigate(l, order, 1);
    Expect(l.Left == "whatsapp" && l.Right == "slack" && l.FocusRight && l.Ratio == .4);
    LayoutActions.Navigate(l, order, -1);
    Expect(l.Left == "telegram" && l.Right == "whatsapp" && !l.FocusRight);
    LayoutActions.Navigate(l, order, -1);
    Expect(l.Left == "slack" && l.Right == "telegram");
    LayoutActions.Navigate(l, order, 1);
    Expect(l.Left == "telegram" && l.Right == "whatsapp");
    LayoutActions.Navigate(l, ["telegram", "whatsapp"], 1);
    Expect(l.Left == "whatsapp" && l.Right == "telegram");
});
Check("Reordering changes navigation without changing visible accounts or profiles", () =>
{
    var a = new Account { ProviderId = "telegram" };
    var b = new Account { ProviderId = "whatsapp" };
    var c = new Account { ProviderId = "slack" };
    var s = new Settings { Accounts = [a, b, c], Layout = new() { Left = a.Id, Right = b.Id, Split = true } };
    Expect(LayoutActions.MoveAccount(s, c.Id, 1));
    Expect(s.Accounts.SequenceEqual(new[] { a, c, b }));
    Expect(s.Layout.Left == a.Id && s.Layout.Right == b.Id);
    s.Layout.Split = false;
    LayoutActions.Navigate(s.Layout, s.Accounts.Select(x => x.Id).ToArray(), 1);
    Expect(s.Layout.Left == c.Id);
    Expect(!LayoutActions.MoveAccount(s, "missing", 0));
});
Check("Alerts do not double-count title badges and unknown activity clears on acknowledgement", () =>
{
    var unread = new UnreadState();
    unread.Receive();
    unread.Receive();
    Expect(unread.Badge.Label("Number") == "•");
    unread.Acknowledge();
    Expect(!unread.Badge.HasActivity);
    unread.UpdateTitle("(4) Telegram");
    unread.Receive();
    Expect(unread.Badge.Label("Number") == "4");
    unread.Acknowledge();
    Expect(unread.Badge.Count == 4);
    unread.UpdateTitle("Telegram");
    Expect(!unread.Badge.HasActivity);
});
Check("Taskbar number, dot, off and unknown totals stay truthful", () =>
{
    var total = BadgeValue.Combine([new(4, false), new(50, false)]);
    Expect(total.Label("Number") == "54" && total.Label("Dot") == "•" && total.Label("Off") == "");
    Expect(BadgeValue.Combine([total, new(0, true)]).Label("Number") == "54+");
    Expect(new BadgeValue(1000, false).Label("Number") == "99+");
    Expect(new BadgeValue(0, false).Label("Dot") == "");
});
Check("Appearance, badge mode and tile order survive restart and invalid values normalize", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "Gather-tests-" + Guid.NewGuid().ToString("N"));
    var store = new SettingsStore(dir);
    var a = new Account { ProviderId = "slack" };
    var b = new Account { ProviderId = "telegram" };
    var s = new Settings { Accounts = [a, b], Theme = "Dark", TaskbarBadge = "Dot" };
    LayoutActions.MoveAccount(s, b.Id, 0);
    store.Save(s);
    var read = store.Load();
    Expect(read.Theme == "Dark" && read.TaskbarBadge == "Dot" && read.Accounts[0].Id == b.Id);
    read.Theme = "bad";
    read.TaskbarBadge = "bad";
    LayoutActions.Normalize(read);
    Expect(read.Theme == "System" && read.TaskbarBadge == "Number");
    File.Delete(Path.Combine(dir, "settings.json"));
    Directory.Delete(dir);
});
Check("Fresh Slack sign-in strips OAuth state and callback codes", () =>
{
    var account = new Account { ProviderId = "slack", StartUrl = "https://oauth2.slack.com/workspace-signin/oauth/google/end?code=fake&state=fake" };
    Expect(NavigationPolicy.FreshSignInUrl(account) == "https://slack.com/signin");
    account.StartUrl = "https://team.slack.com/client?code=fake";
    Expect(NavigationPolicy.FreshSignInUrl(account) == "https://team.slack.com/signin");
});
Check("Atomic settings backup restores account identity after corrupt JSON", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "Gather-recovery-" + Guid.NewGuid().ToString("N"));
    var store = new SettingsStore(dir);
    var account = new Account { ProviderId = "telegram", Name = "Personal" };
    var state = new Settings { Accounts = [account], WindowWidth = 1200, WindowHeight = 800 };
    store.Save(state);
    state.Theme = "Dark";
    store.Save(state);
    File.WriteAllText(Path.Combine(dir, "settings.json"), "{broken");
    var recovered = new SettingsStore(dir);
    var read = recovered.Load();
    Expect(read.Accounts.Single().Id == account.Id && read.WindowWidth == 1200 && recovered.LoadWarning != null);
    Expect(Directory.GetFiles(dir, "settings.json.unreadable-*").Length == 1);
    recovered.Save(read);
    Expect(new SettingsStore(dir).Load().Accounts.Single().ProfileName == account.ProfileName);
    foreach (var file in Directory.GetFiles(dir))
        File.Delete(file);
    Directory.Delete(dir);
});
Check("Window geometry and content size reject unsafe persisted values", () =>
{
    var s = new Settings { WindowWidth = int.MaxValue, WindowHeight = -1, Accounts = [new() { ProviderId = "slack", Zoom = double.NaN }] };
    LayoutActions.Normalize(s);
    Expect(s.WindowWidth == 3840 && s.WindowHeight == 640 && s.Accounts[0].Zoom == 1);
});
QuietChecks.Run(Check);
QuickSwitchChecks.Run(Check);
ShellAppearanceChecks.Run(Check);
PrivacyChecks.Run(Check);
SignalChecks.Run(Check, args);
if (OperatingSystem.IsWindows()) StartupChecks.Run(Check, args.Contains("--skip-registry-checks"));
Console.WriteLine($"{passed} checks passed.");

