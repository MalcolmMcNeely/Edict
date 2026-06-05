using Edict.Contracts.Projections;
using Edict.Core.Correlation;

using Microsoft.Extensions.Time.Testing;

namespace Edict.Core.Tests.Correlation;

public sealed class CursorWaitCoordinatorTests
{
    static readonly DateTimeOffset Now = new(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);
    static readonly Guid CorrelationA = new("aaaaaaaa-0000-0000-0000-000000000001");

    static readonly TimeSpan Bounded = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task WaitAsync_CorrelationAlreadyProcessed_AnswersCursorReachedImmediately()
    {
        // Arrange
        var time = new FakeTimeProvider(Now);
        var coordinator = new CursorWaitCoordinator();
        coordinator.Activate(new Guid[8], head: 0, count: 0);
        coordinator.MarkProcessed(CorrelationA);

        // Act — no clock advance; a caught-up correlation must resolve without waiting.
        var status = await coordinator.WaitAsync(CorrelationA, Bounded, time, CancellationToken.None);

        // Assert
        Assert.Equal(EdictReadStatus.CursorReached, status);
    }

    [Fact]
    public async Task WaitAsync_ParkedThenSignalled_AnswersCursorReached()
    {
        // Arrange
        var time = new FakeTimeProvider(Now);
        var coordinator = new CursorWaitCoordinator();
        coordinator.Activate(new Guid[8], head: 0, count: 0);

        // Act — park first (correlation not yet processed), then signal it.
        var pending = coordinator.WaitAsync(CorrelationA, Bounded, time, CancellationToken.None);
        Assert.False(pending.IsCompleted);

        coordinator.MarkProcessed(CorrelationA);
        var status = await pending;

        // Assert
        Assert.Equal(EdictReadStatus.CursorReached, status);
    }

    [Fact]
    public async Task WaitAsync_BoundedWaitElapses_AnswersCursorTimedOut()
    {
        // Arrange
        var time = new FakeTimeProvider(Now);
        var coordinator = new CursorWaitCoordinator();
        coordinator.Activate(new Guid[8], head: 0, count: 0);

        // Act — park, then advance the virtual clock past the bound with no signal.
        var pending = coordinator.WaitAsync(CorrelationA, Bounded, time, CancellationToken.None);
        Assert.False(pending.IsCompleted);

        time.Advance(Bounded);
        var status = await pending;

        // Assert
        Assert.Equal(EdictReadStatus.CursorTimedOut, status);
    }

    [Fact]
    public async Task WaitAsync_ExplicitIndefinite_DoesNotTimeOut_ResolvesOnlyOnSignal()
    {
        // Arrange
        var time = new FakeTimeProvider(Now);
        var coordinator = new CursorWaitCoordinator();
        coordinator.Activate(new Guid[8], head: 0, count: 0);

        // Act — an indefinite wait must survive an arbitrarily large clock advance.
        var pending = coordinator.WaitAsync(CorrelationA, Timeout.InfiniteTimeSpan, time, CancellationToken.None);
        time.Advance(TimeSpan.FromDays(365));
        Assert.False(pending.IsCompleted);

        coordinator.MarkProcessed(CorrelationA);
        var status = await pending;

        // Assert
        Assert.Equal(EdictReadStatus.CursorReached, status);
    }

    [Fact]
    public async Task WaitAsync_EvictedCorrelation_DegradesToCursorTimedOut()
    {
        // Arrange — a window of two, then process three correlations so the first
        // ages out of the ring. An evicted correlation must degrade to a plain
        // read (a bounded timeout), not error.
        var time = new FakeTimeProvider(Now);
        var coordinator = new CursorWaitCoordinator();
        coordinator.Activate(new Guid[2], head: 0, count: 0);

        var first = new Guid("11111111-0000-0000-0000-000000000001");
        coordinator.MarkProcessed(first);
        coordinator.MarkProcessed(new Guid("22222222-0000-0000-0000-000000000002"));
        coordinator.MarkProcessed(new Guid("33333333-0000-0000-0000-000000000003"));

        // Act — the evicted correlation parks and elapses against the clock.
        var pending = coordinator.WaitAsync(first, Bounded, time, CancellationToken.None);
        time.Advance(Bounded);
        var status = await pending;

        // Assert
        Assert.Equal(EdictReadStatus.CursorTimedOut, status);
    }

    [Theory]
    [InlineData(null, 2)]      // omitted falls back to the bounded default
    [InlineData(5, 5)]         // an explicit value is honoured
    public void ResolveTimeout_FallsBackToBoundedDefaultOnlyWhenOmitted(int? requestedSeconds, int expectedSeconds)
    {
        var requested = requestedSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;

        var resolved = CursorWaitCoordinator.ResolveTimeout(requested, Bounded);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), resolved);
    }

    [Fact]
    public void ResolveTimeout_HonoursExplicitInfinite()
    {
        var resolved = CursorWaitCoordinator.ResolveTimeout(Timeout.InfiniteTimeSpan, Bounded);

        Assert.Equal(Timeout.InfiniteTimeSpan, resolved);
    }

    [Fact]
    public async Task WaitAsync_CallerCancels_ThrowsOperationCanceled()
    {
        // Arrange
        var time = new FakeTimeProvider(Now);
        var coordinator = new CursorWaitCoordinator();
        coordinator.Activate(new Guid[8], head: 0, count: 0);
        using var cancellation = new CancellationTokenSource();

        // Act
        var pending = coordinator.WaitAsync(CorrelationA, Timeout.InfiniteTimeSpan, time, cancellation.Token);
        Assert.False(pending.IsCompleted);
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }
}
