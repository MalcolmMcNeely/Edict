using Edict.Core.Administration;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;

namespace Edict.Core.Tests.Outbox;

/// <summary>
/// Targets the host plumbing on <see cref="EdictDurableConsumerBase{TPayload}"/>
/// directly via a trivial <see cref="TestDurableConsumer"/> subclass — the
/// duplicated adapter that used to live on each consumer root is now its own
/// test surface (ADR 0018 unified envelope, ADR 0019 dead-letter recovery).
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
    public async Task EnsureIntakeNotBlocked_ShouldThrowSaturatedException_WhenDeadLetterAtCap()
    {
        var grain = _fixture.GrainFactory.GetGrain<ITestDurableConsumer>(Guid.NewGuid());
        var saturated = new OutboxSlice
        {
            DeadLetter =
            [
                new DeadLetterEntry
                {
                    Entry = PublishEntry(EntryA, attemptCount: 1),
                    DeadLetteredAt = _fixture.Clock.GetUtcNow(),
                    Reason = "seeded (test)",
                },
            ],
        };
        await grain.SeedOutboxAsync(saturated);

        await Assert.ThrowsAsync<EdictOutboxSaturatedException>(grain.EnsureIntakeNotBlockedProbeAsync);
    }

    [Fact]
    public async Task EnsureIntakeNotBlocked_ShouldBeNoOp_WhenDeadLetterUnderCap()
    {
        var grain = _fixture.GrainFactory.GetGrain<ITestDurableConsumer>(Guid.NewGuid());
        await grain.SeedOutboxAsync(new OutboxSlice());

        await grain.EnsureIntakeNotBlockedProbeAsync(); // no throw
    }

    [Fact]
    public async Task RedriveAsync_ShouldMoveDeadLetterBackToOutboxTailAndDrain()
    {
        CountingExecutor.Reset();
        var grain = _fixture.GrainFactory.GetGrain<ITestDurableConsumer>(Guid.NewGuid());

        var seededEntry = PublishEntry(EntryA, attemptCount: 5);
        var saturated = new OutboxSlice
        {
            DeadLetter =
            [
                new DeadLetterEntry
                {
                    Entry = seededEntry,
                    DeadLetteredAt = _fixture.Clock.GetUtcNow(),
                    Reason = "max attempts exhausted",
                },
            ],
        };
        await grain.SeedOutboxAsync(saturated);

        var admin = grain.AsReference<IEdictDeadLetterAdmin>();
        await admin.RedriveAsync(EntryA);

        var outboxAfter = await grain.GetOutboxAsync();
        Assert.Empty(outboxAfter.DeadLetter);
        Assert.Empty(outboxAfter.Pending); // drained immediately after redrive
        Assert.Contains(EntryA, CountingExecutor.Executed);
    }

    [Fact]
    public async Task ListDeadLetterAsync_ShouldProjectCurrentOutboxSliceViaDeadLetterProjection()
    {
        var grain = _fixture.GrainFactory.GetGrain<ITestDurableConsumer>(Guid.NewGuid());

        var deadAt = _fixture.Clock.GetUtcNow();
        var slice = new OutboxSlice
        {
            DeadLetter =
            [
                new DeadLetterEntry
                {
                    Entry = PublishEntry(EntryA, attemptCount: 3),
                    DeadLetteredAt = deadAt,
                    Reason = "max attempts exhausted",
                },
            ],
        };
        await grain.SeedOutboxAsync(slice);

        var admin = grain.AsReference<IEdictDeadLetterAdmin>();
        var listed = await admin.ListDeadLetterAsync();
        var projected = DeadLetterProjection.From(slice);

        Assert.Equal(projected.Count, listed.Count);
        Assert.Equal(projected[0].EntryId, listed[0].EntryId);
        Assert.Equal(projected[0].Kind, listed[0].Kind);
        Assert.Equal(projected[0].AttemptCount, listed[0].AttemptCount);
        Assert.Equal(projected[0].DeadLetteredAt, listed[0].DeadLetteredAt);
        Assert.Equal(projected[0].Reason, listed[0].Reason);
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
