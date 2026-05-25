using System.Runtime.CompilerServices;

using Microsoft.Extensions.Time.Testing;

namespace Edict.Core.Tests.Commands;

public sealed class CommandHandlerRaiseTests
{
    static readonly Guid OrderId = new("33333333-3333-3333-3333-333333333333");
    static readonly DateTimeOffset RaiseTime = new(2026, 5, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Raise_ShouldStampOccurredAtAtRaiseTime_NotAtAnyLaterDrainTime()
    {
        var time = new FakeTimeProvider();
        time.SetUtcNow(RaiseTime);

        var handler = NewHandlerWithoutOrleans(time);

        await handler.Handle(new PlaceOrderCommand(OrderId, "WIDGET"));

        // Simulate any subsequent outbox delay — the stamp must not change.
        time.Advance(TimeSpan.FromMinutes(30));

        var raised = handler._raisedEvents;
        Assert.NotNull(raised);
        var single = Assert.Single(raised);
        Assert.Equal(RaiseTime, single.OccurredAt);
    }

    [Fact]
    public async Task Raise_AcrossThreeCallsWith1msBetween_ShouldYieldStrictlyIncreasingOccurredAt()
    {
        var time = new FakeTimeProvider();
        time.SetUtcNow(RaiseTime);

        var handler = NewHandlerWithoutOrleans(time);

        await handler.Handle(new PlaceOrderCommand(OrderId, "A"));
        time.Advance(TimeSpan.FromMilliseconds(1));
        await handler.Handle(new PlaceOrderCommand(OrderId, "B"));
        time.Advance(TimeSpan.FromMilliseconds(1));
        await handler.Handle(new PlaceOrderCommand(OrderId, "C"));

        var raised = handler._raisedEvents;
        Assert.NotNull(raised);
        Assert.Equal(3, raised.Count);
        Assert.Equal(RaiseTime, raised[0].OccurredAt);
        Assert.Equal(RaiseTime.AddMilliseconds(1), raised[1].OccurredAt);
        Assert.Equal(RaiseTime.AddMilliseconds(2), raised[2].OccurredAt);
    }

    // Orleans' Grain constructor reads RuntimeContext.Current and NREs outside
    // an activation. The TimeProvider seam on EdictCommandHandler is the only
    // dependency Raise() pulls in this test, so bypass the constructor and
    // inject the fake clock directly — Handle/Raise touch neither State nor
    // ServiceProvider.
    static OrderCommandHandler NewHandlerWithoutOrleans(TimeProvider time)
    {
        var handler = (OrderCommandHandler)RuntimeHelpers.GetUninitializedObject(typeof(OrderCommandHandler));
        handler._timeProvider = time;
        return handler;
    }
}
