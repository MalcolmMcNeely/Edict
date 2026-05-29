using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

using Edict.Telemetry;

namespace Edict.Core.Metrics;

/// <summary>
/// Static holder for the three Slice-2 observable gauges. Mirrors the
/// <c>OutboxDrainMetrics</c> / <c>IdempotencyDedupMetrics</c> shape so the
/// architecture-test fact that scans for static instrument fields discovers
/// the instruments here, and so the static initialiser runs once per process
/// rather than once per cache instance. Live <see cref="EdictMetricsCache"/>
/// instances register themselves on construction; the gauge callbacks
/// aggregate across every registered cache (every silo in this process —
/// usually one, parallel TestClusters add transient extras).
/// </summary>
static class EdictMetricsCacheGauges
{
    static readonly object Lock = new();
    static readonly List<WeakReference<EdictMetricsCache>> Caches = [];

    public static readonly ObservableGauge<int> PendingCount = EdictDiagnostics.Meter.CreateObservableGauge(
        SemanticConventions.Outbox.Meters.PendingCount,
        ObservePendingCount);

    public static readonly ObservableGauge<double> OldestEntryAge = EdictDiagnostics.Meter.CreateObservableGauge(
        SemanticConventions.Outbox.Meters.OldestEntryAge,
        ObserveOldestEntryAge);

    public static readonly ObservableGauge<double> SagaProgressAge = EdictDiagnostics.Meter.CreateObservableGauge(
        SemanticConventions.Sagas.Meters.ProgressAge,
        ObserveSagaProgressAge);

    public static void Register(EdictMetricsCache cache)
    {
        lock (Lock)
        {
            Caches.RemoveAll(w => !w.TryGetTarget(out _));
            Caches.Add(new WeakReference<EdictMetricsCache>(cache));
        }
    }

    static IReadOnlyList<EdictMetricsCache> Snapshot()
    {
        lock (Lock)
        {
            var result = new List<EdictMetricsCache>(Caches.Count);
            foreach (var w in Caches)
            {
                if (w.TryGetTarget(out var c)) { result.Add(c); }
            }
            return result;
        }
    }

    static IEnumerable<Measurement<int>> ObservePendingCount()
    {
        var perType = new Dictionary<string, int>();
        foreach (var cache in Snapshot())
        {
            cache.AccumulatePendingCount(perType);
        }
        foreach (var (type, sum) in perType)
        {
            yield return new Measurement<int>(
                sum,
                new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, type));
        }
    }

    static IEnumerable<Measurement<double>> ObserveOldestEntryAge()
    {
        var perType = new Dictionary<string, double>();
        foreach (var cache in Snapshot())
        {
            cache.AccumulateOldestEntryAge(perType);
        }
        foreach (var (type, age) in perType)
        {
            yield return new Measurement<double>(
                age,
                new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, type));
        }
    }

    static IEnumerable<Measurement<double>> ObserveSagaProgressAge()
    {
        var perType = new Dictionary<string, double>();
        foreach (var cache in Snapshot())
        {
            cache.AccumulateSagaProgressAge(perType);
        }
        foreach (var (type, age) in perType)
        {
            yield return new Measurement<double>(
                age,
                new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, type));
        }
    }
}

/// <summary>
/// In-process, <see cref="ConcurrentDictionary{TKey,TValue}"/>-backed
/// <see cref="IEdictMetricsCache"/> implementation. Registers itself with
/// <see cref="EdictMetricsCacheGauges"/> on construction so the three
/// silo-local observable gauges have a source to read at scrape time.
/// </summary>
sealed class EdictMetricsCache : IEdictMetricsCache
{
    readonly ConcurrentDictionary<(string GrainType, string GrainKey), OutboxState> _outbox = new();
    readonly ConcurrentDictionary<(string SagaType, string SagaKey), DateTimeOffset> _saga = new();
    readonly TimeProvider _timeProvider;

    public EdictMetricsCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        EdictMetricsCacheGauges.Register(this);
    }

    public void ReportOutbox(string grainType, string grainKey, int pendingCount, DateTimeOffset? oldestEnqueuedAt)
    {
        if (pendingCount == 0)
        {
            _outbox.TryRemove((grainType, grainKey), out _);
            return;
        }

        _outbox[(grainType, grainKey)] = new OutboxState(pendingCount, oldestEnqueuedAt);
    }

    public void ReportSaga(string sagaType, string sagaKey, DateTimeOffset lastHandledAt)
    {
        _saga[(sagaType, sagaKey)] = lastHandledAt;
    }

    public void Remove(string grainType, string grainKey)
    {
        _outbox.TryRemove((grainType, grainKey), out _);
        _saga.TryRemove((grainType, grainKey), out _);
    }

    /// <summary>Aggregate outbox depth + oldest enqueue per grain type. Test/probe
    /// surface for <c>Edict.Testing</c>; not on the consumer-facing
    /// <see cref="IEdictMetricsCache"/> contract.</summary>
    public (int Pending, DateTimeOffset? Oldest) GetOutboxState(string grainType)
    {
        var sum = 0;
        DateTimeOffset? oldest = null;
        foreach (var ((type, _), state) in _outbox)
        {
            if (type != grainType) { continue; }
            sum += state.PendingCount;
            if (state.OldestEnqueuedAt is { } enq && (oldest is null || enq < oldest))
            {
                oldest = enq;
            }
        }
        return (sum, oldest);
    }

    public (int TotalPending, DateTimeOffset? OldestEnqueuedAt) GetOutboxStateAggregate()
    {
        var sum = 0;
        DateTimeOffset? oldest = null;
        foreach (var (_, state) in _outbox)
        {
            sum += state.PendingCount;
            if (state.OldestEnqueuedAt is { } enqueued && (oldest is null || enqueued < oldest))
            {
                oldest = enqueued;
            }
        }
        return (sum, oldest);
    }

    /// <summary>Probe surface for <c>Edict.Testing</c>: most-recent last-handled
    /// timestamp across every active saga of <paramref name="sagaType"/>.</summary>
    public DateTimeOffset? GetSagaState(string sagaType)
    {
        DateTimeOffset? mostRecent = null;
        foreach (var ((type, _), at) in _saga)
        {
            if (type != sagaType) { continue; }
            if (mostRecent is null || at > mostRecent)
            {
                mostRecent = at;
            }
        }
        return mostRecent;
    }

    internal void AccumulatePendingCount(Dictionary<string, int> perType)
    {
        foreach (var ((type, _), state) in _outbox)
        {
            perType.TryGetValue(type, out var sum);
            perType[type] = sum + state.PendingCount;
        }
    }

    internal void AccumulateOldestEntryAge(Dictionary<string, double> perType)
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var ((type, _), state) in _outbox)
        {
            if (state.OldestEnqueuedAt is not { } enq) { continue; }
            var ageSeconds = (now - enq).TotalSeconds;
            if (!perType.TryGetValue(type, out var current) || ageSeconds > current)
            {
                perType[type] = ageSeconds;
            }
        }
    }

    internal void AccumulateSagaProgressAge(Dictionary<string, double> perType)
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var ((type, _), at) in _saga)
        {
            var ageSeconds = (now - at).TotalSeconds;
            if (!perType.TryGetValue(type, out var current) || ageSeconds > current)
            {
                perType[type] = ageSeconds;
            }
        }
    }

    readonly record struct OutboxState(int PendingCount, DateTimeOffset? OldestEnqueuedAt);
}
