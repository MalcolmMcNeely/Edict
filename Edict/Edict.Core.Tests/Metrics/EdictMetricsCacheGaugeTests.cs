using System.Diagnostics.Metrics;

using Edict.Core.Metrics;
using Edict.Telemetry;

using Microsoft.Extensions.Time.Testing;

namespace Edict.Core.Tests.Metrics;

public sealed class EdictMetricsCacheGaugeTests
{
    static readonly DateTimeOffset Now = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PendingCount_ShouldAggregateSumPerGrainType_AcrossMultipleGrains()
    {
        var marker = $"MetricsCacheTest_{Guid.NewGuid():N}";
        var time = new FakeTimeProvider(Now);
        var cache = new EdictMetricsCache(time);

        cache.ReportOutbox(marker, "grain-1", pendingCount: 3, oldestEnqueuedAt: Now.AddSeconds(-10));
        cache.ReportOutbox(marker, "grain-2", pendingCount: 5, oldestEnqueuedAt: Now.AddSeconds(-2));

        var captures = new List<Capture<int>>();
        using var listener = StartIntListener(SemanticConventions.Outbox.Meters.PendingCount, marker, captures);
        listener.RecordObservableInstruments();

        var capture = Assert.Single(captures);
        Assert.Equal(8, capture.Value);
        Assert.Equal(marker, capture.Tag(SemanticConventions.Common.Tags.GrainType));
    }

    [Fact]
    public void PendingCount_ShouldDropEntry_WhenReportedWithZeroPending()
    {
        var marker = $"MetricsCacheTest_{Guid.NewGuid():N}";
        var time = new FakeTimeProvider(Now);
        var cache = new EdictMetricsCache(time);

        cache.ReportOutbox(marker, "grain-1", pendingCount: 4, oldestEnqueuedAt: Now);
        cache.ReportOutbox(marker, "grain-1", pendingCount: 0, oldestEnqueuedAt: null);

        var captures = new List<Capture<int>>();
        using var listener = StartIntListener(SemanticConventions.Outbox.Meters.PendingCount, marker, captures);
        listener.RecordObservableInstruments();

        Assert.Empty(captures);
    }

    [Fact]
    public void Remove_ShouldDropEntry_SoDeactivatedGrainStopsContributing()
    {
        var marker = $"MetricsCacheTest_{Guid.NewGuid():N}";
        var time = new FakeTimeProvider(Now);
        var cache = new EdictMetricsCache(time);

        cache.ReportOutbox(marker, "grain-1", pendingCount: 7, oldestEnqueuedAt: Now);
        cache.Remove(marker, "grain-1");

        var captures = new List<Capture<int>>();
        using var listener = StartIntListener(SemanticConventions.Outbox.Meters.PendingCount, marker, captures);
        listener.RecordObservableInstruments();

        Assert.Empty(captures);
    }

    [Fact]
    public void OldestEntryAge_ShouldReportMaxAgePerGrainType()
    {
        var marker = $"MetricsCacheTest_{Guid.NewGuid():N}";
        var time = new FakeTimeProvider(Now);
        var cache = new EdictMetricsCache(time);

        cache.ReportOutbox(marker, "grain-1", pendingCount: 1, oldestEnqueuedAt: Now.AddSeconds(-7));
        cache.ReportOutbox(marker, "grain-2", pendingCount: 1, oldestEnqueuedAt: Now.AddSeconds(-3));

        var captures = new List<Capture<double>>();
        using var listener = StartDoubleListener(SemanticConventions.Outbox.Meters.OldestEntryAge, marker, captures);
        listener.RecordObservableInstruments();

        var capture = Assert.Single(captures);
        Assert.Equal(7.0, capture.Value, precision: 3);
        Assert.Equal(marker, capture.Tag(SemanticConventions.Common.Tags.GrainType));
    }

    [Fact]
    public void OldestEntryAge_ShouldGrow_AsTimeAdvancesWithoutReport()
    {
        var marker = $"MetricsCacheTest_{Guid.NewGuid():N}";
        var time = new FakeTimeProvider(Now);
        var cache = new EdictMetricsCache(time);

        cache.ReportOutbox(marker, "grain-1", pendingCount: 1, oldestEnqueuedAt: Now);

        time.Advance(TimeSpan.FromSeconds(13));

        var captures = new List<Capture<double>>();
        using var listener = StartDoubleListener(SemanticConventions.Outbox.Meters.OldestEntryAge, marker, captures);
        listener.RecordObservableInstruments();

        var capture = Assert.Single(captures);
        Assert.Equal(13.0, capture.Value, precision: 3);
    }

    [Fact]
    public void SagaProgressAge_ShouldReportMaxAgePerSagaType_AndGrowWithFakeTime()
    {
        var marker = $"MetricsCacheTest_{Guid.NewGuid():N}";
        var time = new FakeTimeProvider(Now);
        var cache = new EdictMetricsCache(time);

        cache.ReportSaga(marker, "saga-a", lastHandledAt: Now.AddSeconds(-5));
        cache.ReportSaga(marker, "saga-b", lastHandledAt: Now);

        time.Advance(TimeSpan.FromSeconds(10));

        var captures = new List<Capture<double>>();
        using var listener = StartDoubleListener(SemanticConventions.Sagas.Meters.ProgressAge, marker, captures);
        listener.RecordObservableInstruments();

        var capture = Assert.Single(captures);
        // saga-a was last handled at Now-5s; after Advance(+10s) the saga-a entry is 15s old,
        // which is the max across the saga type.
        Assert.Equal(15.0, capture.Value, precision: 3);
        Assert.Equal(marker, capture.Tag(SemanticConventions.Common.Tags.GrainType));
    }

    [Fact]
    public void GetOutboxState_ShouldExposeAggregate_ForTestingHarnessProbe()
    {
        var marker = $"MetricsCacheTest_{Guid.NewGuid():N}";
        var time = new FakeTimeProvider(Now);
        var cache = new EdictMetricsCache(time);

        cache.ReportOutbox(marker, "grain-1", pendingCount: 2, oldestEnqueuedAt: Now.AddSeconds(-4));
        cache.ReportOutbox(marker, "grain-2", pendingCount: 6, oldestEnqueuedAt: Now.AddSeconds(-1));

        var (pending, oldest) = cache.GetOutboxState(marker);

        Assert.Equal(8, pending);
        Assert.Equal(Now.AddSeconds(-4), oldest);
    }

    [Fact]
    public void GetSagaState_ShouldExposeMostRecentTimestamp_ForTestingHarnessProbe()
    {
        var marker = $"MetricsCacheTest_{Guid.NewGuid():N}";
        var time = new FakeTimeProvider(Now);
        var cache = new EdictMetricsCache(time);

        cache.ReportSaga(marker, "saga-a", lastHandledAt: Now.AddSeconds(-7));
        cache.ReportSaga(marker, "saga-b", lastHandledAt: Now.AddSeconds(-2));

        var mostRecent = cache.GetSagaState(marker);

        Assert.Equal(Now.AddSeconds(-2), mostRecent);
    }

    static MeterListener StartIntListener(string instrumentName, string grainTypeMarker, List<Capture<int>> captures)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName && inst.Name == instrumentName)
                {
                    l.EnableMeasurementEvents(inst);
                }
            },
        };
        listener.SetMeasurementEventCallback<int>((inst, value, tags, _) =>
        {
            if (inst.Name != instrumentName) { return; }
            var dict = ToDict(tags);
            if ((dict.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string) == grainTypeMarker)
            {
                lock (captures) { captures.Add(new Capture<int>(value, dict)); }
            }
        });
        listener.Start();
        return listener;
    }

    static MeterListener StartDoubleListener(string instrumentName, string grainTypeMarker, List<Capture<double>> captures)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName && inst.Name == instrumentName)
                {
                    l.EnableMeasurementEvents(inst);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
        {
            if (inst.Name != instrumentName) { return; }
            var dict = ToDict(tags);
            if ((dict.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string) == grainTypeMarker)
            {
                lock (captures) { captures.Add(new Capture<double>(value, dict)); }
            }
        });
        listener.Start();
        return listener;
    }

    static Dictionary<string, object?> ToDict(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dict = new Dictionary<string, object?>(tags.Length);
        foreach (var t in tags)
        {
            dict[t.Key] = t.Value;
        }
        return dict;
    }

    sealed record Capture<T>(T Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var v) ? v : null;
    }
}
