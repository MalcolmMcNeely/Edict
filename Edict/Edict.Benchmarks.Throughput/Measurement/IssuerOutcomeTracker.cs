using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Edict.Benchmarks.Throughput.Measurement;

/// <summary>
/// Per-point failure tracker shared across the issuer task fan-out. Each
/// issuer reports its own outcome at end-of-window via <see cref="Merge"/>;
/// the first occurrence of each exception type triggers a one-line stderr
/// log so the operator sees the leading-indicator fault during the run
/// rather than only at the markdown rollup.
/// </summary>
sealed class IssuerOutcomeTracker
{
    readonly string _label;
    readonly TextWriter _diagnosticSink;
    readonly ConcurrentDictionary<string, byte> _typesAlreadyLogged = new(StringComparer.Ordinal);
    readonly object _lock = new();
    long _succeeded;
    long _failed;
    readonly SortedDictionary<string, long> _failuresByType = new(StringComparer.Ordinal);

    public IssuerOutcomeTracker(string label, TextWriter? diagnosticSink = null)
    {
        _label = label;
        _diagnosticSink = diagnosticSink ?? Console.Error;
    }

    public void RecordSuccess()
    {
        Interlocked.Increment(ref _succeeded);
    }

    public void RecordFailure(Exception exception)
    {
        var typeName = exception.GetType().Name;
        Interlocked.Increment(ref _failed);
        lock (_lock)
        {
            _failuresByType.TryGetValue(typeName, out var current);
            _failuresByType[typeName] = current + 1;
        }
        if (_typesAlreadyLogged.TryAdd(typeName, 0))
        {
            _diagnosticSink.WriteLine(
                $"[bench] {_label}: first {typeName} — {exception.Message}");
        }
    }

    public RunHealth Build()
    {
        lock (_lock)
        {
            return new RunHealth(
                Succeeded: Interlocked.Read(ref _succeeded),
                Failed: Interlocked.Read(ref _failed),
                FailuresByType: _failuresByType.ToImmutableSortedDictionary(StringComparer.Ordinal));
        }
    }
}
