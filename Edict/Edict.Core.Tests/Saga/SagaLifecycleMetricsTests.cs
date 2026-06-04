using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Telemetry;

namespace Edict.Core.Tests.Saga;

// Lifecycle-counter emission, proven through the in-process TestCluster: the
// silo runs in the test process so a process-global MeterListener sees the
// counters EdictSaga emits directly. Bound to the SagaLifecycle collection so it
// runs serially with SagaLifecycleTests — neither class can pollute the other's
// saga-type-filtered captures.
[Collection(SagaLifecycleCollection.Name)]
public sealed class SagaLifecycleMetricsTests
{
    readonly SagaLifecycleClusterFixture _fixture;

    public SagaLifecycleMetricsTests(SagaLifecycleClusterFixture fixture)
    {
        _fixture = fixture;
        CapturingPublishEventExecutor.Reset();
        CapturingSendCommandExecutor.Reset();
    }

    static LifecycleTriggerEvent Trigger(Guid workflowId) =>
        new(workflowId) { EventId = Guid.NewGuid() };

    static LifecycleFinishEvent Finish(Guid workflowId) =>
        new(workflowId) { EventId = Guid.NewGuid() };

    [Fact]
    public async Task Complete_EmitsCompletedCounter_TaggedBySagaType()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetDefaultCapSaga(workflowId);
        var captures = new CaptureSink();
        using var listener = StartListener(SemanticConventions.Sagas.Meters.Completed, captures);

        await saga.DeliverAsync(Trigger(workflowId));
        await saga.DeliverAsync(Finish(workflowId));

        var capture = await captures.SingleForAsync(typeof(DefaultCapSaga));
        Assert.Equal(1L, capture.Value);
    }

    [Fact]
    public async Task FiredCap_WithNoOverride_EmitsTimeoutFired_TaggedDeadlettered()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetCappedSaga(workflowId);
        var captures = new CaptureSink();
        using var listener = StartListener(SemanticConventions.Sagas.Meters.TimeoutFired, captures);

        await saga.DeliverAsync(Trigger(workflowId));
        SagaLifecycleClusterFixture.Time.Advance(TimeSpan.FromMinutes(2));
        await saga.FireCapAsync();

        var capture = await captures.SingleForAsync(typeof(CappedSaga));
        Assert.Equal(1L, capture.Value);
        Assert.Equal(
            SemanticConventions.Sagas.Tags.OutcomeValues.Deadlettered,
            capture.Tag(SemanticConventions.Sagas.Tags.Outcome));
    }

    [Fact]
    public async Task FiredCap_WithOverride_EmitsTimeoutFired_TaggedCompensated()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetCompensatingSaga(workflowId);
        var captures = new CaptureSink();
        using var listener = StartListener(SemanticConventions.Sagas.Meters.TimeoutFired, captures);

        await saga.DeliverAsync(Trigger(workflowId));
        SagaLifecycleClusterFixture.Time.Advance(TimeSpan.FromMinutes(2));
        await saga.FireCapAsync();

        var capture = await captures.SingleForAsync(typeof(CompensatingSaga));
        Assert.Equal(1L, capture.Value);
        Assert.Equal(
            SemanticConventions.Sagas.Tags.OutcomeValues.Compensated,
            capture.Tag(SemanticConventions.Sagas.Tags.Outcome));
    }

    static MeterListener StartListener(string instrumentName, CaptureSink captures)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, enabler) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName && instrument.Name == instrumentName)
                {
                    enabler.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name != instrumentName)
            {
                return;
            }
            var dictionary = new Dictionary<string, object?>(tags.Length);
            foreach (var tag in tags)
            {
                dictionary[tag.Key] = tag.Value;
            }
            captures.Add(new Capture(value, dictionary));
        });
        listener.Start();
        return listener;
    }

    ISagaLifecycleProbe GetDefaultCapSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(DefaultCapSaga).FullName);

    ISagaLifecycleProbe GetCappedSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(CappedSaga).FullName);

    ISagaLifecycleProbe GetCompensatingSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(CompensatingSaga).FullName);

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var value) ? value : null;
    }

    // The counter fires on the grain-activation thread inside the in-process silo,
    // so a measurement can land a scheduler tick after the awaited grain call has
    // already returned to the test thread. The sink guards the list against that
    // cross-thread write and SingleForAsync polls briefly for the saga-typed
    // capture rather than reading once — the same async-observation tolerance the
    // sibling span test applies to lagging ActivityStopped callbacks. The poll
    // returns on the first match, so a healthy run pays nothing.
    sealed class CaptureSink
    {
        readonly List<Capture> _captures = [];
        readonly Lock _gate = new();

        public void Add(Capture capture)
        {
            lock (_gate)
            {
                _captures.Add(capture);
            }
        }

        public async Task<Capture> SingleForAsync(Type sagaType)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var match = SnapshotFor(sagaType);
                if (match.Count > 0)
                {
                    return Assert.Single(match);
                }
                await Task.Delay(TimeSpan.FromMilliseconds(20));
            }
            return Assert.Single(SnapshotFor(sagaType));
        }

        IReadOnlyList<Capture> SnapshotFor(Type sagaType)
        {
            lock (_gate)
            {
                return _captures
                    .Where(capture => (capture.Tag(SemanticConventions.Common.Tags.GrainType) as string) == sagaType.FullName)
                    .ToList();
            }
        }
    }
}
