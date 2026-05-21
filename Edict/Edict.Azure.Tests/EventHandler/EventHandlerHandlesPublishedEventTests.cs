using Edict.Contracts.Events;

namespace Edict.Azure.Tests.EventHandler;

/// <summary>
/// Azurite/Testcontainers conformance for the <c>EdictEventHandler</c>
/// stream-callback path (ADR 0023). Publishing a handled event onto the real
/// Azure Queue stream provider lands one invocation of the consumer's
/// <c>Handle</c> — observably, via the handler probe — without any
/// in-memory shortcut. Lifted from <c>EdictEventHandlerStreamCallbackTests</c>
/// in Core.Tests so the proof exercises the substrate the sample silo wires
/// in production.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class EventHandlerHandlesPublishedEventTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task EventHandler_ShouldRunHandleExactlyOnce_WhenAzureQueueDeliversHandledEvent()
    {
        var customerId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailEventPublisher>(customerId);
        var handler = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailHandlerProbe>(customerId);

        var eventId = Guid.NewGuid();
        var evt = new AzureCustomerNotifiedEvent(customerId, "welcome") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(evt);

        var handled = await EventHandlerWaiters.WaitForHandledAsync(handler);
        Assert.Single(handled);
        Assert.Equal(eventId, handled[0]);
    }
}
