using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Metrics;
using Edict.Core.Outbox;
using Edict.Core.Tests.TestSupport;

using Microsoft.Extensions.Time.Testing;

using Orleans.Streams;

namespace Edict.Core.Tests.Metrics;

public sealed class OutboxHostMetricsCacheTests
{
    static readonly DateTimeOffset Now = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);
    static readonly EdictOptions Options = new();

    [Fact]
    public async Task EnqueueAndDrainAsync_GatedEntries_ShouldReportPendingAndEarliestEnqueuedAt()
    {
        var cache = new FakeMetricsCache();
        var time = new FakeTimeProvider(Now);
        var host = BuildHost(cache, time, new SuccessfulExecutor(), grainTypeName: "MyGrain", grainKey: "k1");

        await host.EnqueueAndDrainAsync(
        [
            Entry(1) with { NextAttemptUtc = Now.AddMinutes(5) },
            Entry(2) with { NextAttemptUtc = Now.AddMinutes(5) },
        ]);

        var last = cache.OutboxReports.Last(r => r.GrainType == "MyGrain" && r.GrainKey == "k1");
        Assert.Equal(2, last.PendingCount);
        Assert.Equal(Now, last.OldestEnqueuedAt);
    }

    [Fact]
    public async Task EnqueueAndDrainAsync_CleanDrain_ShouldReportZeroPending()
    {
        var cache = new FakeMetricsCache();
        var time = new FakeTimeProvider(Now);
        var host = BuildHost(cache, time, new SuccessfulExecutor(), grainTypeName: "MyGrain", grainKey: "k2");

        await host.EnqueueAndDrainAsync([Entry(1), Entry(2)]);

        var last = cache.OutboxReports.Last(r => r.GrainType == "MyGrain" && r.GrainKey == "k2");
        Assert.Equal(0, last.PendingCount);
        Assert.Null(last.OldestEnqueuedAt);
    }

    [Fact]
    public async Task EnqueueAndDrainAsync_ShouldStampEnqueuedAt_FromHostTimeProvider()
    {
        var cache = new FakeMetricsCache();
        var time = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var host = BuildHost(cache, time, executor, grainTypeName: "MyGrain", grainKey: "k3");

        await host.EnqueueAndDrainAsync([Entry(1), Entry(2)]);

        Assert.NotEmpty(executor.Invocations);
        Assert.All(executor.Invocations, e => Assert.Equal(Now, e.EnqueuedAt));
    }

    [Fact]
    public async Task OnDeactivateAsync_ShouldCallCacheRemove_ForThisGrain()
    {
        var cache = new FakeMetricsCache();
        var time = new FakeTimeProvider(Now);
        var host = BuildHost(cache, time, new SuccessfulExecutor(), grainTypeName: "MyGrain", grainKey: "k4");

        await host.EnqueueAndDrainAsync([Entry(1) with { NextAttemptUtc = Now.AddMinutes(5) }]);
        await host.OnDeactivateAsync();

        var removal = Assert.Single(cache.Removals);
        Assert.Equal("MyGrain", removal.GrainType);
        Assert.Equal("k4", removal.GrainKey);
    }

    static OutboxHost<EdictUnit> BuildHost(
        IEdictMetricsCache cache,
        TimeProvider time,
        IOutboxEffectExecutor executor,
        string grainTypeName,
        string grainKey)
    {
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var reminders = new RecordingReminderRegistrar(log);
        return new OutboxHost<EdictUnit>(
            state,
            NullStreamProvider.Instance,
            reminders,
            [executor],
            Options,
            time,
            new NoopPromoter(),
            grainKey: grainKey,
            grainTypeName: grainTypeName,
            metricsCache: cache);
    }

    static OutboxEntry Entry(int seed) => new()
    {
        EntryId = new Guid(seed, (short)0, (short)0, 0, 0, 0, 0, 0, 0, 0, 0),
        Kind = OutboxEffectKind.PublishEvent,
        Payload = [(byte)seed],
    };

    sealed class FakeMetricsCache : IEdictMetricsCache
    {
        public List<OutboxReport> OutboxReports { get; } = [];
        public List<SagaReport> SagaReports { get; } = [];
        public List<Removal> Removals { get; } = [];

        public void ReportOutbox(string grainType, string grainKey, int pendingCount, DateTimeOffset? oldestEnqueuedAt) =>
            OutboxReports.Add(new OutboxReport(grainType, grainKey, pendingCount, oldestEnqueuedAt));

        public void ReportSaga(string sagaType, string sagaKey, DateTimeOffset lastHandledAt) =>
            SagaReports.Add(new SagaReport(sagaType, sagaKey, lastHandledAt));

        public void Remove(string grainType, string grainKey) =>
            Removals.Add(new Removal(grainType, grainKey));

        public (int TotalPending, DateTimeOffset? OldestEnqueuedAt) GetOutboxStateAggregate() => (0, null);
    }

    sealed record OutboxReport(string GrainType, string GrainKey, int PendingCount, DateTimeOffset? OldestEnqueuedAt);
    sealed record SagaReport(string SagaType, string SagaKey, DateTimeOffset LastHandledAt);
    sealed record Removal(string GrainType, string GrainKey);

    sealed class SuccessfulExecutor : IOutboxEffectExecutor
    {
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;
        public Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent) =>
            Task.CompletedTask;
    }

    sealed class RecordingExecutor : IOutboxEffectExecutor
    {
        readonly List<OutboxEntry> _invocations = [];
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;
        public IReadOnlyList<OutboxEntry> Invocations => _invocations;
        public Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
        {
            _invocations.Add(entry);
            return Task.CompletedTask;
        }
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
            throw new NotSupportedException();
    }
}
