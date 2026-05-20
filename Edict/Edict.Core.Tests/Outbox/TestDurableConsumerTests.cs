using Edict.Core.Outbox;

namespace Edict.Core.Tests.Outbox;

/// <summary>
/// Targets the host plumbing on <see cref="EdictDurableConsumerBase{TPayload}"/>
/// directly via a trivial <see cref="TestDurableConsumer"/> subclass — the
/// duplicated adapter that used to live on each consumer root is now its own
/// test surface (ADR 0018 unified envelope, ADR 0022 forensic-only dead
/// letters: the in-grain DeadLetter slice and operator-recovery surface are
/// gone, so the host plumbing this exercises is just drain-on-activation and
/// reminder ticking).
/// </summary>
public sealed class TestDurableConsumerTests : IClassFixture<TestDurableConsumerClusterFixture>
{
    static readonly Guid EntryA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid EntryB = new("bbbbbbbb-0000-0000-0000-000000000002");

    readonly TestDurableConsumerClusterFixture _fixture;

    public TestDurableConsumerTests(TestDurableConsumerClusterFixture fixture)
    {
        _fixture = fixture;
    }

    static OutboxEntry PublishEntry(Guid id, int attemptCount = 0) => new()
    {
        EntryId = id,
        Kind = OutboxEffectKind.PublishEvent,
        Payload = [1, 2, 3],
        AttemptCount = attemptCount,
    };

    [Fact]
    public async Task OnActivateAsync_ShouldDrain_WhenOutboxIsNonEmpty()
    {
        CountingExecutor.Reset();
        var grainId = Guid.NewGuid();
        var grain = _fixture.GrainFactory.GetGrain<ITestDurableConsumer>(grainId);

        // Seed pending entries, deactivate, then re-activate via a probe call.
        await grain.SeedOutboxAsync(new OutboxSlice().Enqueue(PublishEntry(EntryA)).Enqueue(PublishEntry(EntryB)));
        await grain.DeactivateAndWaitAsync();

        // Re-acquire and call a probe — activation runs drain-on-activation.
        var reactivated = _fixture.GrainFactory.GetGrain<ITestDurableConsumer>(grainId);
        var outboxAfter = await reactivated.GetOutboxAsync();

        Assert.Contains(EntryA, CountingExecutor.Executed);
        Assert.Contains(EntryB, CountingExecutor.Executed);
        Assert.Empty(outboxAfter.Pending);
    }

    [Fact]
    public async Task OnActivateAsync_ShouldSkipDrain_WhenOutboxIsEmpty()
    {
        CountingExecutor.Reset();
        var grain = _fixture.GrainFactory.GetGrain<ITestDurableConsumer>(Guid.NewGuid());

        // Probe call activates the grain; outbox is empty so drain is skipped.
        var outboxAfter = await grain.GetOutboxAsync();

        Assert.Empty(CountingExecutor.Executed);
        Assert.Empty(outboxAfter.Pending);
    }

    [Fact]
    public async Task ReceiveReminder_ShouldDrainAndKeepReminderRegistered()
    {
        CountingExecutor.Reset();
        var grain = _fixture.GrainFactory.GetGrain<ITestDurableConsumer>(Guid.NewGuid());

        await grain.SeedOutboxAsync(new OutboxSlice().Enqueue(PublishEntry(EntryA)));

        await grain.TriggerReminderAsync();

        var outboxAfter = await grain.GetOutboxAsync();
        Assert.Contains(EntryA, CountingExecutor.Executed);
        Assert.Empty(outboxAfter.Pending);
    }
}
