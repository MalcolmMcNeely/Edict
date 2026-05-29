using System.Diagnostics.Metrics;

using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.Time.Testing;

using Orleans.Streams;

namespace Edict.Core.Tests.Outbox;

public sealed class OutboxDrainMetricsTests
{
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
    static readonly EdictOptions Options = new();

    [Fact]
    public async Task DrainAsync_SinglePassWithThreeEntries_ShouldEmitOneCountAndOneEntriesObservation()
    {
        var marker = $"OutboxDrainMetricsTest_{Guid.NewGuid():N}";
        var counts = new List<Capture<long>>();
        var entries = new List<Capture<int>>();
        using var listener = StartListener(marker, counts, entries);

        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var host = BuildHost(state, log, new SuccessfulExecutor(), grainTypeName: marker);

        await host.EnqueueAndDrainAsync([Entry(1), Entry(2), Entry(3)]);

        var count = Assert.Single(counts);
        Assert.Equal(1L, count.Value);
        Assert.Equal(marker, count.Tag(SemanticConventions.Common.Tags.GrainType));

        var entriesObservation = Assert.Single(entries);
        Assert.Equal(3, entriesObservation.Value);
        Assert.Equal(marker, entriesObservation.Tag(SemanticConventions.Common.Tags.GrainType));
    }

    [Fact]
    public async Task DrainAsync_AllEntriesGated_ShouldEmitNoMetric()
    {
        var marker = $"OutboxDrainMetricsTest_{Guid.NewGuid():N}";
        var counts = new List<Capture<long>>();
        var entries = new List<Capture<int>>();
        using var listener = StartListener(marker, counts, entries);

        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var host = BuildHost(state, log, new SuccessfulExecutor(), grainTypeName: marker);

        await host.EnqueueAndDrainAsync(
        [
            Entry(1) with { NextAttemptUtc = Now.AddMinutes(5) },
            Entry(2) with { NextAttemptUtc = Now.AddMinutes(5) },
        ]);

        Assert.Empty(counts);
        Assert.Empty(entries);
    }

    static OutboxHost<EdictUnit> BuildHost(
        CountingPersistentState<GrainEnvelope<EdictUnit>> state,
        CallLog log,
        IOutboxEffectExecutor executor,
        string grainTypeName)
    {
        var time = new FakeTimeProvider(Now);
        var reminders = new RecordingReminderRegistrar(log);
        return new OutboxHost<EdictUnit>(
            state,
            NullStreamProvider.Instance,
            reminders,
            [executor],
            Options,
            time,
            new NoopPromoter(),
            grainKey: "test-grain",
            grainTypeName: grainTypeName);
    }

    static OutboxEntry Entry(int seed) => new()
    {
        EntryId = new Guid(seed, (short)0, (short)0, 0, 0, 0, 0, 0, 0, 0, 0),
        Kind = OutboxEffectKind.PublishEvent,
        Payload = [(byte)seed],
    };

    static MeterListener StartListener(string grainTypeMarker, List<Capture<long>> counts, List<Capture<int>> entries)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name != EdictDiagnostics.SourceName)
                {
                    return;
                }
                if (inst.Name == SemanticConventions.Outbox.Meters.DrainCount
                    || inst.Name == SemanticConventions.Outbox.Meters.DrainEntries)
                {
                    l.EnableMeasurementEvents(inst);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            if (inst.Name != SemanticConventions.Outbox.Meters.DrainCount) { return; }
            var dict = ToDict(tags);
            if ((dict.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string) == grainTypeMarker)
            {
                counts.Add(new Capture<long>(value, dict));
            }
        });
        listener.SetMeasurementEventCallback<int>((inst, value, tags, _) =>
        {
            if (inst.Name != SemanticConventions.Outbox.Meters.DrainEntries) { return; }
            var dict = ToDict(tags);
            if ((dict.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string) == grainTypeMarker)
            {
                entries.Add(new Capture<int>(value, dict));
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

    sealed class SuccessfulExecutor : IOutboxEffectExecutor
    {
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;
        public Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent) =>
            Task.CompletedTask;
    }

    sealed class NoopPromoter : IDeadLetterPromoter
    {
        public OutboxEntry Promote(OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now) =>
            failed with { Kind = OutboxEffectKind.PublishEvent };
    }

    sealed class NullStreamProvider : IStreamProvider
    {
        public static readonly NullStreamProvider Instance = new();
        public string Name => "edict";
        public bool IsRewindable => false;
        public IAsyncStream<T> GetStream<T>(StreamId streamId) =>
            throw new NotSupportedException("NullStreamProvider has no streams.");
    }
}
