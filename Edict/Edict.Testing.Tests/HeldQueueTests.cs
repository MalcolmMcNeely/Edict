using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Testing.Internal;

using Xunit;

namespace Edict.Testing.Tests;

public sealed class HeldQueueTests
{
    [EdictStream("HeldQueueProbe")]
    sealed partial record E1 : EdictEvent
    {
        [EdictRouteKey]
        public Guid Id { get; init; } = Guid.Empty;
    }

    [EdictStream("HeldQueueProbe")]
    sealed partial record E2 : EdictEvent
    {
        [EdictRouteKey]
        public Guid Id { get; init; } = Guid.Empty;
    }

    [EdictStream("HeldQueueProbe")]
    sealed partial record E3 : EdictEvent
    {
        [EdictRouteKey]
        public Guid Id { get; init; } = Guid.Empty;
    }

    [Fact]
    public void OnArrival_ReturnsJustTheArrival_WhenHoldCountIsZero()
    {
        // Arrange
        var queue = new HeldQueue();
        var subscriber = new object();
        var evt = new E1();

        // Act
        var released = queue.OnArrival(subscriber, evt, holdCount: 0);

        // Assert
        Assert.Equal([evt], released);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void OnArrival_ReleasesHeldEvent_AfterKSubsequentArrivalsToSameSubscriber()
    {
        // Arrange
        var queue = new HeldQueue();
        var subscriber = new object();
        var held = new E1();
        var pass1 = new E2();
        var pass2 = new E3();

        // Act
        var afterHold = queue.OnArrival(subscriber, held, holdCount: 2);
        var afterFirstSubsequent = queue.OnArrival(subscriber, pass1, holdCount: 0);
        var afterSecondSubsequent = queue.OnArrival(subscriber, pass2, holdCount: 0);

        // Assert
        Assert.Empty(afterHold);
        Assert.Equal([pass1], afterFirstSubsequent);
        // The held event releases on the 2nd subsequent arrival, in arrival order.
        Assert.Equal([(EdictEvent)held, pass2], afterSecondSubsequent);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void OnArrival_DoesNotDecrementHolds_OnOtherSubscribers()
    {
        // Arrange — production substrate is per-subscriber-queue; a parallel
        // subscriber's arrivals must not move another subscriber's counters.
        var queue = new HeldQueue();
        var subscriberX = new object();
        var subscriberY = new object();
        var heldOnX = new E1();
        var passOnY = new E2();

        // Act
        var afterHold = queue.OnArrival(subscriberX, heldOnX, holdCount: 1);
        var afterCrossArrival = queue.OnArrival(subscriberY, passOnY, holdCount: 0);

        // Assert — Y's arrival does not release X's held event.
        Assert.Empty(afterHold);
        Assert.Equal([passOnY], afterCrossArrival);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void FlushAll_ReleasesEverythingInArrivalOrder_AndEmptiesTheQueue()
    {
        // Arrange
        var queue = new HeldQueue();
        var subscriberX = new object();
        var subscriberY = new object();
        var first = new E1();
        var second = new E2();
        var third = new E3();

        queue.OnArrival(subscriberX, first, holdCount: 5);
        queue.OnArrival(subscriberY, second, holdCount: 5);
        queue.OnArrival(subscriberX, third, holdCount: 5);

        // Act
        var flushed = queue.FlushAll();

        // Assert — arrival order, regardless of subscriber.
        Assert.Equal(
            [
                (subscriberX, (EdictEvent)first),
                (subscriberY, (EdictEvent)second),
                (subscriberX, (EdictEvent)third),
            ],
            flushed);
        Assert.Equal(0, queue.Count);

        // After FlushAll the bookkeeping is fresh: the next held event behaves
        // as if the queue had just been constructed.
        var fresh = new E1();
        var afterFlush = queue.OnArrival(subscriberX, fresh, holdCount: 0);
        Assert.Equal([fresh], afterFlush);
    }
}
