using Edict.Telemetry;
using Edict.Tests.Conformance.EventHandler;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance that exactly-once staging survives <em>duplication</em>,
/// not only the distinct-event concurrent burst
/// <see cref="ProjectionDeliverySerializationScenarios{TFixture}"/> already pins. A
/// duplicate of one id rides the concurrent burst alongside the distinct ids, and the
/// same id is redelivered again once the burst has settled. Both duplicates must be
/// suppressed by the dedup ring: the handler runs each distinct id exactly once and the
/// duplicated id no more than the rest, so the final handled set is a function of the
/// id <em>set</em>, never of how many times any id was delivered.
/// </summary>
public abstract class ExactlyOnceUnderRedeliveryScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected ExactlyOnceUnderRedeliveryScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DuplicatedEventId_IsHandledOnce_WhenRedeliveredDuringAndAfterAConcurrentBurst()
    {
        const int distinctCount = 8;
        var customerId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IEmailEventPublisher>(customerId);
        var handler = _fixture.GrainFactory.GetGrain<IEmailHandlerProbe>(customerId);

        var distinct = Enumerable.Range(0, distinctCount)
            .Select(index => new CustomerNotifiedEvent(customerId, $"notice-{index}") with
            {
                EventId = Guid.NewGuid(),
                OccurredAt = DateTimeOffset.UtcNow,
            })
            .ToArray();
        var duplicatedId = distinct[0].EventId;

        // Arrange + Act — burst every distinct id and, riding the same burst, a duplicate
        // of the first id. Publishing without awaiting between sends gives the duplicate
        // the realest chance to interleave the original's delivery; whichever lands second
        // is the one the ring suppresses.
        var burst = distinct.Select(publisher.PublishAsync).ToList();
        burst.Add(publisher.PublishAsync(new CustomerNotifiedEvent(customerId, "duplicate-during") with
        {
            EventId = duplicatedId,
            OccurredAt = DateTimeOffset.UtcNow,
        }));
        await Task.WhenAll(burst);

        await EmailHandlerWaiters.WaitForHandledAsync(handler, expectedCount: distinctCount);

        // Act — redeliver the same id after the burst has settled. A fresh capture opened
        // here sees only this redelivery's suppression span, so waiting on it proves the
        // after-duplicate reached the ring and was dropped without racing the burst's own
        // during-duplicate span.
        using var capture = new SpanCapture();
        await publisher.PublishAsync(new CustomerNotifiedEvent(customerId, "duplicate-after") with
        {
            EventId = duplicatedId,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Deduplicated} CustomerNotifiedEvent",
            "deduplicated span for the redelivered CustomerNotifiedEvent");

        // Assert — every distinct id ran exactly once; neither the during nor the after
        // duplicate inflated the count. The handled set equalling the distinct id set is
        // the no-double-count invariant under duplication.
        var handled = await handler.GetHandledEventIdsAsync();
        Assert.Equal(distinctCount, handled.Count);
        Assert.Equal(distinct.Select(edictEvent => edictEvent.EventId).ToHashSet(), handled.ToHashSet());
    }
}
