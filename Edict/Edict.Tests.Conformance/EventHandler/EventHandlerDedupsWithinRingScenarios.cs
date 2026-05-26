using Edict.Contracts.Events;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.EventHandler;

/// <summary>
/// Substrate-agnostic conformance for at-most-once *staging* over at-least-once
/// delivery: the <c>EdictIdempotencyBase</c> dedup ring suppresses redelivery
/// of the same <c>EventId</c> even when the bound substrate's stream provider
/// re-delivers. Re-stage of the InvokeHandler entry would run <c>Handle</c>
/// twice; the ring guards against that.
/// </summary>
public abstract class EventHandlerDedupsWithinRingScenarios<TFixture>
    where TFixture : ConformanceFixture
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

        var eventId = Guid.NewGuid();
        var evt = new CustomerNotifiedEvent(customerId, "first") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var duplicate = new CustomerNotifiedEvent(customerId, "duplicate-marker") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(evt);
        await EmailHandlerWaiters.WaitForHandledAsync(handler);

        await publisher.PublishAsync(duplicate);
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Single(await handler.GetHandledEventIdsAsync());
    }
}
