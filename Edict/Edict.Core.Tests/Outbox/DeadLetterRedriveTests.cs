using Edict.Contracts.Commands;
using Edict.Core.Administration;
using Edict.Core.Tests.Grains;

namespace Edict.Core.Tests.Outbox;

// Operator recovery (ADR 0019): the dedicated IEdictDeadLetterAdmin grain
// method redrives a dead-lettered entry — atomic DeadLetter→Outbox with the
// attempt counter reset. With the downstream now healthy the redriven entry
// publishes and the slice self-clears (unblocking intake). In-memory cluster,
// no Azurite (ADR 0016).
[Collection(DeadLetterCapClusterCollection.Name)]
public sealed class DeadLetterRedriveTests(DeadLetterCapClusterFixture fixture)
{
    [Fact]
    public async Task RedriveAsync_ShouldMoveDeadLetterBackToOutboxAndPublish_WhenDownstreamHealthy()
    {
        var id = Guid.NewGuid();
        var probe = fixture.Cluster.GrainFactory.GetGrain<ICounterProbe>(id);

        // Drive one entry to dead-letter (MaxAttempts=1 fixture).
        ControllableOutboxExecutor.FailedAttempts = 0;
        ControllableOutboxExecutor.ShouldFail = true;
        try
        {
            await fixture.Sender.Send(new IncrementCounterCommand(id));

            var deadIds = await probe.GetDeadLetterEntryIdsAsync();
            var deadEntryId = Assert.Single(deadIds);

            // Downstream heals; operator redrives via the dedicated admin
            // interface, targeting this aggregate's grain class.
            ControllableOutboxExecutor.ShouldFail = false;
            var admin = fixture.Cluster.GrainFactory.GetGrain<IEdictDeadLetterAdmin>(
                id, "Edict.Core.Tests.Grains.CounterAggregate");

            await admin.RedriveAsync(deadEntryId);

            // The entry left DeadLetter, went back to the Outbox, and the now
            // healthy downstream published it: it was acked (pending 0) and
            // NOT re-dead-lettered (DeadLetter 0) — a non-throwing ack means
            // PublishEventExecutor's OnNextAsync genuinely succeeded. The
            // aggregate State (count 1) confirms the command's effect stands.
            Assert.Equal(0, await probe.GetDeadLetterCountAsync());
            Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
            Assert.Equal(1, await probe.GetCountAsync());
        }
        finally
        {
            ControllableOutboxExecutor.ShouldFail = false;
        }
    }
}
