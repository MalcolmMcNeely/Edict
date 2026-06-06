using Edict.Telemetry;
using Edict.Tests.Conformance.EventHandler;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance for at-most-once <em>staging</em> over
/// at-least-once delivery: the <c>EdictIdempotencyBase</c> dedup ring suppresses
/// redelivery of the same <c>EventId</c> even when the bound streaming provider
/// re-delivers. Re-stage of the InvokeHandler entry would run <c>Handle</c>
/// twice; the ring guards against that. This is the dedup-over-redelivery
/// contract proven against a real stream.
/// </summary>
public abstract class EventHandlerDedupsWithinRingScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected EventHandlerDedupsWithinRingScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EventHandler_ShouldSuppressDuplicate_WhenSameEventIdRedelivered()
    {
        var customerId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IEmailEventPublisher>(customerId);
        var handler = _fixture.GrainFactory.GetGrain<IEmailHandlerProbe>(customerId);
        using var capture = new SpanCapture();

        var eventId = Guid.NewGuid();
        var edictEvent = new CustomerNotifiedEvent(customerId, "first") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var duplicate = new CustomerNotifiedEvent(customerId, "duplicate-marker") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        // Handle the first delivery and let its dedup-ring entry commit before the
        // redelivery arrives, so the duplicate hits a populated ring rather than
        // racing the first event's own staging.
        await publisher.PublishAsync(edictEvent);
        await EmailHandlerWaiters.WaitForHandledAsync(handler);

        // Suppression emits the edict.event.deduplicated span; waiting on it proves
        // the redelivery reached the ring and was dropped, the positive signal a fixed
        // delay only stood in for. CustomerNotifiedEvent dedups in no other scenario
        // and the collection runs serially, so the span is unambiguously this turn's.
        await publisher.PublishAsync(duplicate);
        await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Deduplicated} CustomerNotifiedEvent",
            "deduplicated span for the redelivered CustomerNotifiedEvent");

        // The handler ran for the first delivery only; the redelivery never reached it.
        var handledEventId = Assert.Single(await handler.GetHandledEventIdsAsync());
        Assert.Equal(eventId, handledEventId);
    }
}
