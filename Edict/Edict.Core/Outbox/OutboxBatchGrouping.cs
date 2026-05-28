namespace Edict.Core.Outbox;

/// <summary>
/// Pure deep module: groups consecutive Outbox entries that share a key, so
/// the drain can dispatch a multi-event same-stream burst as one
/// <c>OnNextBatchAsync</c> instead of N <c>OnNextAsync</c>. Consecutive-only:
/// non-contiguous matches are never merged because insertion order is
/// load-bearing for at-most-out-of-order delivery semantics.
/// </summary>
static class OutboxBatchGrouping
{
    /// <summary>
    /// Walks <paramref name="entries"/> once and accumulates consecutive
    /// entries with equal <paramref name="keySelector"/> output into a group.
    /// Insertion order is preserved within each group and across groups.
    /// </summary>
    public static IReadOnlyList<(string StreamName, Guid RouteKey, IReadOnlyList<OutboxEntry> Entries)> Group(
        IReadOnlyList<OutboxEntry> entries,
        Func<OutboxEntry, (string StreamName, Guid RouteKey)> keySelector)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var groups = new List<(string, Guid, IReadOnlyList<OutboxEntry>)>();
        var currentKey = keySelector(entries[0]);
        var currentEntries = new List<OutboxEntry> { entries[0] };

        for (var i = 1; i < entries.Count; i++)
        {
            var entry = entries[i];
            var key = keySelector(entry);
            if (key == currentKey)
            {
                currentEntries.Add(entry);
                continue;
            }

            groups.Add((currentKey.StreamName, currentKey.RouteKey, currentEntries));
            currentKey = key;
            currentEntries = [entry];
        }

        groups.Add((currentKey.StreamName, currentKey.RouteKey, currentEntries));
        return groups;
    }
}
