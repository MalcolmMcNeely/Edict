using Edict.Contracts.Events;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.EventHandler;

/// <summary>
/// Substrate-agnostic conformance for the unhandled-event-type branch: when the
/// bound substrate's stream delivers an event the handler has no
/// <c>Handle</c> overload for, the framework must not stage an
/// <c>InvokeHandler</c> entry or take a ring slot — observably, the probe
/// stays at zero.
/// </summary>
public abstract class EventHandlerNoOpForUnhandledTypeScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected EventHandlerNoOpForUnhandledTypeScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EventHandler_ShouldNotInvokeHandle_WhenStreamDeliversUnhandledEventType()
    {
        var aggregateId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IEmailEventPublisher>(aggregateId);
        var handler = _fixture.GrainFactory.GetGrain<IEmailHandlerProbe>(aggregateId);

        var unhandled = new UnhandledEmailEvent(aggregateId, 1) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(unhandled);

        // A re-stage via InvokeHandler would land within the fixture's queue
        // visibility window; 3s here is enough to confirm no dispatch.
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await handler.GetHandledCountAsync());
    }
}
