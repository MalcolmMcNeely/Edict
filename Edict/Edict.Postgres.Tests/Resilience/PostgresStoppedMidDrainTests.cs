using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Outbox;

namespace Edict.Postgres.Tests.Resilience;

// The Postgres connection-drop fault mode, exercised against the outbox
// drain. A one-shot synthetic publish failure only stages a durable
// pending outbox entry and arms the drain reminder; the fault under test is the
// real Postgres outage on the recovery drain's state write-back. With Postgres
// stopped the recovery drain publishes the event to the AQS stream but cannot
// persist the ack, so the entry stays pending and the activation is dropped.
// Once Postgres restarts, a reminder tick reconnects, the ack persists, and the
// outbox settles to empty with the reminder unregistered.
//
// Because the publish precedes the Postgres ack-write, a drain-window outage
// always lands post-publish and the recovery re-drain re-publishes. EventId is
// now assigned once as the event enters the Outbox and carried on the persisted
// payload, so that re-publish carries the same id and the consumer dedup ring
// collapses it: this pins effectively-once across the ack-write-window fault on
// a real substrate, not just at-least-once.
[Collection(PostgresResilienceCollection.Name)]
public sealed class PostgresStoppedMidDrainTests(PostgresResilienceClusterFixture fixture)
{
    [Fact]
    public async Task DrainRecovery_AppliesExactlyOnce_WhenPostgresStoppedMidDrainForcesRepublish()
    {
        await fixture.EnsureRunningAsync();
        ControllableOutboxExecutor.Reset();

        var counterId = Guid.NewGuid();
        var probe = fixture.Cluster.GrainFactory.GetGrain<ICounterProbe>(counterId);
        var applied = fixture.Cluster.GrainFactory.GetGrain<ICounterAppliedProbe>(counterId);

        // Arrange — the first publish fails, so the command commits state and
        // leaves the raised event as a durable pending outbox entry with the
        // drain reminder armed. State committed while Postgres was healthy.
        ControllableOutboxExecutor.ShouldFail = true;
        var result = await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        Assert.IsType<EdictCommandResult.Accepted>(result);
        Assert.Equal(1, await probe.GetCountAsync());
        await PostgresResilienceWaiters.WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);
        Assert.True(await probe.HasDrainReminderAsync());

        // Act — let the publish succeed, stop Postgres, then drive a reminder
        // tick. The publish reaches the stream but the ack write-back faults
        // against the stopped backend and rolls back, so the entry stays pending.
        ControllableOutboxExecutor.ShouldFail = false;
        await fixture.StopPostgresAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => probe.ForceDrainViaReminderAsync());

        // Recover — once Postgres is back, a reminder tick reconnects through the
        // existing data source and completes the drain. The recovery re-drain
        // re-publishes the same event (a fresh publish over the still-pending
        // entry).
        await fixture.StartPostgresAsync();
        await PostgresResilienceWaiters.WaitUntilAsync(async () =>
        {
            try
            {
                await probe.ForceDrainViaReminderAsync();
                return await probe.GetPendingOutboxCountAsync() == 0;
            }
            catch
            {
                return false;
            }
        });

        // Assert — the outbox drained to empty and the reminder unregistered.
        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
        Assert.False(await probe.HasDrainReminderAsync());

        // The re-published event carries the same EventId as the original
        // publish, so the consumer's dedup ring collapses it: the handler applies
        // exactly once across the publish / re-publish, not twice.
        await PostgresResilienceWaiters.WaitUntilAsync(async () => await applied.GetAppliedCountAsync() >= 1);
        Assert.Equal(1, await applied.GetAppliedCountAsync());
    }
}
