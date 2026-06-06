using System.Diagnostics.Metrics;

using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Xunit;

namespace Edict.Core.Tests.Idempotency;

// The event-handler staging path must record edict.idempotency.duplicate.count
// with a reason tag on a suppressed redelivery, matching the projection, saga,
// and pointer-envelope paths. These white-box tests drive the real delivery seam
// and assert the counter the unified dedup guard emits, so reverting the guard on
// the staging path turns one of them red.
[Collection(SubstrateIndependentCollection.Name)]
public sealed class EventHandlerDedupReasonTests
{
    readonly SubstrateIndependentClusterFixture _fixture;

    public EventHandlerDedupReasonTests(SubstrateIndependentClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RedeliveryOfCommittedEventId_RecordsExactlyOneDuplicateCount_WithWindowReason()
    {
        var aggregateId = Guid.NewGuid();
        var consumer = _fixture.GrainFactory.GetGrain<IEventHandlerDedupReasonConsumer>(aggregateId);
        var captures = new List<Capture>();
        using var listener = StartDuplicateCountListener(captures, nameof(WindowReasonEvent));

        var edictEvent = new WindowReasonEvent(aggregateId) { EventId = Guid.NewGuid() };
        await consumer.DeliverAsync(edictEvent);
        await consumer.DeliverAsync(edictEvent);

        var capture = Assert.Single(captures);
        Assert.Equal(1L, capture.Value);
        Assert.Equal(nameof(WindowReasonEvent), capture.Tag(SemanticConventions.Events.Tags.Type));
        Assert.Equal(typeof(EventHandlerDedupReasonConsumer).FullName, capture.Tag(SemanticConventions.Common.Tags.GrainType));
        Assert.Equal(SemanticConventions.Idempotency.Tags.ReasonValues.Window, capture.Tag(SemanticConventions.Idempotency.Tags.Reason));
    }

    [Fact]
    public async Task DeliveryWithPreClaimedRingSlot_RecordsExactlyOneDuplicateCount_WithInFlightReason()
    {
        var aggregateId = Guid.NewGuid();
        var consumer = _fixture.GrainFactory.GetGrain<IEventHandlerDedupReasonConsumer>(aggregateId);
        var captures = new List<Capture>();
        using var listener = StartDuplicateCountListener(captures, nameof(InFlightReasonEvent));

        // Defense-in-depth: with the slot held in-flight (never committed), the
        // delivery's claim fails and suppression tags in_flight rather than window.
        // ADR-0062 makes concurrent same-id delivery structurally impossible, so the
        // reservation is pre-armed deterministically rather than raced.
        var eventId = Guid.NewGuid();
        await consumer.ClaimInFlightAsync(eventId);
        await consumer.DeliverAsync(new InFlightReasonEvent(aggregateId) { EventId = eventId });

        var capture = Assert.Single(captures);
        Assert.Equal(1L, capture.Value);
        Assert.Equal(nameof(InFlightReasonEvent), capture.Tag(SemanticConventions.Events.Tags.Type));
        Assert.Equal(typeof(EventHandlerDedupReasonConsumer).FullName, capture.Tag(SemanticConventions.Common.Tags.GrainType));
        Assert.Equal(SemanticConventions.Idempotency.Tags.ReasonValues.InFlight, capture.Tag(SemanticConventions.Idempotency.Tags.Reason));
    }

    static MeterListener StartDuplicateCountListener(List<Capture> captures, string eventTypeName)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, enabler) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.Idempotency.Meters.DuplicateCount)
                {
                    enabler.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var dictionary = new Dictionary<string, object?>(tags.Length);
            foreach (var tag in tags)
            {
                dictionary[tag.Key] = tag.Value;
            }
            if ((dictionary.GetValueOrDefault(SemanticConventions.Events.Tags.Type) as string) == eventTypeName)
            {
                captures.Add(new Capture(value, dictionary));
            }
        });
        listener.Start();
        return listener;
    }

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var value) ? value : null;
    }
}
