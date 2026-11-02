namespace Gather.Core;

public enum NavigationDecision
{
    Internal, External, IdentityBrowser, Block
}
public static class NavigationPolicy
{
    public static NavigationDecision Decide(Provider provider, string? address)
    {
        if (provider.Kind != ProviderKind.Web)
            return NavigationDecision.Block;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            return NavigationDecision.Block;
        if (!uri.IsDefaultPort)
            return NavigationDecision.Block;
        string host = uri.IdnHost.ToLowerInvariant();
        if (host is "accounts.google.com" or "appleid.apple.com" or "account.apple.com")
            return NavigationDecision.IdentityBrowser;
        if (provider.Hosts.Any(h => host == h || (provider.Id == "slack" && h == "slack.com" && host.EndsWith(".slack.com", StringComparison.Ordinal))))
            return NavigationDecision.Internal;
        return NavigationDecision.External;
    }
    public static string StartUrl(Account account)
    {
        var provider = Providers.Find(account.ProviderId)!;
        return account.StartUrl != null && Decide(provider, account.StartUrl) == NavigationDecision.Internal ? account.StartUrl : provider.Url;
    }
    public static int? UnreadCount(string title)
    {
        var match = System.Text.RegularExpressions.Regex.Match(title, @"^\((\d{1,6})\)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var count) ? count : null;
    }
    public static string FreshSignInUrl(Account account)
    {
        var provider = Providers.Find(account.ProviderId)!;
        // Restart at the provider; never forward an OAuth request/callback
        // containing state or codes into a different cookie/session context.
        if (provider.Id != "slack")
            return provider.Url;
        if (Uri.TryCreate(account.StartUrl, UriKind.Absolute, out var uri)
            && Decide(provider, account.StartUrl) == NavigationDecision.Internal
            && uri.Host.EndsWith(".slack.com", StringComparison.OrdinalIgnoreCase)
            && uri.Host is not ("app.slack.com" or "oauth2.slack.com"))
            return "https://" + uri.IdnHost + "/signin";
        return provider.Url;
    }
}
