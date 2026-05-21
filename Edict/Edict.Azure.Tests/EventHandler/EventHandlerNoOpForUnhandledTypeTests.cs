using Edict.Contracts.Events;

namespace Edict.Azure.Tests.EventHandler;

/// <summary>
/// Generator-emitted <c>HandlesType</c> gates the stream callback: an event
/// type the consumer has no <c>Handle</c> overload for is a pure no-op — no
/// ring slot consumed, no InvokeHandler entry staged, no eventual handler
/// invocation. Exercises the real Azure Queue stream provider so the gate
/// holds end-to-end on the substrate the sample silo wires in production.
/// Lifted from <c>EdictEventHandlerStreamCallbackTests</c> in Core.Tests.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class EventHandlerNoOpForUnhandledTypeTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task EventHandler_ShouldNotInvokeHandle_WhenAzureQueueDeliversUnhandledEventType()
    {
        var aggregateId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailEventPublisher>(aggregateId);
        var handler = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailHandlerProbe>(aggregateId);

        // AzureUnhandledEvent has no Handle on AzureEmailEventHandler →
        // HandlesType returns false. Publishing it onto the handler's
        // implicit stream ("AzureEmailEvents") exercises the type-check
        // gate over the real transport.
        var unhandled = new AzureUnhandledEvent(aggregateId, 1) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(unhandled);

        // The handler should never run. A bounded wait is enough — if a
        // re-stage were going to happen via the InvokeHandler executor it
        // would land within the visibility-timeout window of the test
        // fixture's Azure Queue stream (5s).
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await handler.GetHandledCountAsync());
    }
}
