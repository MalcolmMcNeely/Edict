namespace Edict.Core.Tests.TestSupport;

// Monotonic-index recorder shared across persistent-state and reminder shims.
// Each Record returns the slot's index so tests can assert ordering between
// writes and reminder reconciles without touching wall-clock or relying on
// observation order.
sealed class CallLog
{
    readonly List<(string SourceTag, string Method)> _entries = [];

    public int Record(string sourceTag, string method)
    {
        _entries.Add((sourceTag, method));
        return _entries.Count - 1;
    }

    public IReadOnlyList<(string SourceTag, string Method)> Entries => _entries;

    public int Count => _entries.Count;

    public int LastIndexOf(string method)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Method == method)
            {
                return i;
            }
        }
        return -1;
    }
}
