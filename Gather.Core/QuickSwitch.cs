namespace Gather.Core;

// Only account metadata enters the switcher; sessions and messages are never searched.
public static class QuickSwitch
{
    public const int RecentLimit = 20;

    public static IReadOnlyList<Account> Search(Settings state, string? query)
    {
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var recent = state.RecentAccountIds;
        return state.Accounts
            .Where(a => words.All(word => a.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                || (Providers.Find(a.ProviderId)?.Name.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderBy(a => { int index = recent.IndexOf(a.Id); return index < 0 ? int.MaxValue : index; })
            .ToArray();
    }

    public static bool Remember(Settings state, string id)
    {
        if (!state.Accounts.Any(a => a.Id == id) || state.RecentAccountIds.FirstOrDefault() == id) return false;
        state.RecentAccountIds.RemoveAll(x => x == id);
        state.RecentAccountIds.Insert(0, id);
        Normalize(state);
        return true;
    }

    public static void Normalize(Settings state)
    {
        var ids = state.Accounts.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        state.RecentAccountIds = (state.RecentAccountIds ?? []).Where(ids.Contains)
            .Distinct(StringComparer.Ordinal).Take(RecentLimit).ToList();
    }
}
