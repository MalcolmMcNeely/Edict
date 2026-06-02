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
// This pins the producer-side recovery guarantee. It is deliberately NOT an
// end-to-end exactly-once assertion: in DrainAsync the publish precedes the
// Postgres ack-write, so a drain-window outage always lands post-publish and
// the recovery re-drain re-publishes. PublishEventExecutor stamps a fresh
// EventId per publish, so that re-publish is a distinct event the consumer
// dedup ring cannot collapse — at-least-once, not exactly-once. That gap is
// tracked separately.
[Collection(PostgresResilienceCollection.Name)]
public sealed class PostgresStoppedMidDrainTests(PostgresResilienceClusterFixture fixture)
{
    [Fact]
    public async Task DrainRecovery_ReminderReconnectsAndDrainsToEmpty_WhenPostgresStoppedMidDrain()
    {
        await fixture.EnsureRunningAsync();
        ControllableOutboxExecutor.Reset();

        var counterId = Guid.NewGuid();
        var probe = fixture.Cluster.GrainFactory.GetGrain<ICounterProbe>(counterId);

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
        // existing data source and completes the drain. Every probe call here
        // reactivates and reads durable state, so all of them must run after the
        // backend is reachable again — hence both are inside the retry.
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

        // Assert — the reminder loop reconnected and drained the outbox to empty,
        // unregistering the drain reminder.
        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
        Assert.False(await probe.HasDrainReminderAsync());
    }
}
