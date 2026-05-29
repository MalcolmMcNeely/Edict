using Edict.Contracts.Events;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.EventHandler;

/// <summary>
/// Substrate-agnostic conformance for the <c>EdictEventHandler</c>
/// stream-callback path. Publishing a handled event onto the bound substrate's
/// stream provider lands one invocation of the consumer's <c>Handle</c> —
/// observably, via the handler probe — without any in-memory shortcut.
/// </summary>
public abstract class EventHandlerHandlesPublishedEventScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected EventHandlerHandlesPublishedEventScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EventHandler_ShouldRunHandleExactlyOnce_WhenStreamDeliversHandledEvent()
    {
        var customerId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IEmailEventPublisher>(customerId);
        var handler = _fixture.GrainFactory.GetGrain<IEmailHandlerProbe>(customerId);

        var eventId = Guid.NewGuid();
        var edictEvent = new CustomerNotifiedEvent(customerId, "welcome") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(edictEvent);

        var handled = await EmailHandlerWaiters.WaitForHandledAsync(handler);
        Assert.Single(handled);
        Assert.Equal(eventId, handled[0]);
    }
}
