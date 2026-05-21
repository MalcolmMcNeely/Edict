using Edict.Contracts.Events;

namespace Edict.Azure.Tests.EventHandler;

/// <summary>
/// ADR 0023 at-most-once *staging* over ADR 0002 at-least-once delivery: the
/// EdictIdempotencyBase dedup ring suppresses redelivery of the same EventId
/// even when the Azure Queue stream provider re-delivers (visibility-timeout
/// expiry or duplicate publish). Re-stage of the InvokeHandler entry would
/// run <c>Handle</c> twice; the ring guards against that. Lifted from
/// <c>EdictEventHandlerStreamCallbackTests</c> in Core.Tests so the dedup
/// proof exercises the real Azure Queue transport.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class EventHandlerDedupsWithinRingTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task EventHandler_ShouldSuppressDuplicate_WhenSameEventIdRedeliveredViaAzureQueue()
    {
        var customerId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailEventPublisher>(customerId);
        var handler = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailHandlerProbe>(customerId);

        var eventId = Guid.NewGuid();
        var evt = new AzureCustomerNotifiedEvent(customerId, "first") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var duplicate = new AzureCustomerNotifiedEvent(customerId, "duplicate-marker") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(evt);
        await EventHandlerWaiters.WaitForHandledAsync(handler);

        await publisher.PublishAsync(duplicate);
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Single(await handler.GetHandledEventIdsAsync());
    }
}
