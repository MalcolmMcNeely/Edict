using Edict.Contracts.Commands;
using Edict.Core.Administration;
using Edict.Core.DeadLetter;
using Edict.Core.Tests.Grains;

namespace Edict.Core.Tests.Outbox;

// The read-only IEdictDeadLetterRepository seam (ADR 0019) is grain-backed —
// the slice lives only in grain state (no external store), so the repo
// delegates to IEdictDeadLetterAdmin. Proves inspect-after-dead-letter and
// that an operator redrive is reflected. In-memory cluster, no Azurite.
[Collection(DeadLetterCapClusterCollection.Name)]
public sealed class DeadLetterRepositoryTests(DeadLetterCapClusterFixture fixture)
{
    [Fact]
    public async Task ListAsync_ShouldReturnDeadLetteredEntryThenReflectRedrive()
    {
        var id = Guid.NewGuid();
        var repo = new GrainDeadLetterRepository(
            fixture.Cluster.GrainFactory, "Edict.Core.Tests.Grains.CounterAggregate");

        ControllableOutboxExecutor.FailedAttempts = 0;
        ControllableOutboxExecutor.ShouldFail = true;
        try
        {
            await fixture.Sender.Send(new IncrementCounterCommand(id));

            var deadLettered = await repo.ListAsync(id.ToString());
            var entry = Assert.Single(deadLettered);
            Assert.Equal("PublishEvent", entry.Kind);
            Assert.True(entry.AttemptCount >= 1);
            Assert.False(string.IsNullOrEmpty(entry.Reason));

            // Operator redrives; the read seam reflects the now-empty slice.
            ControllableOutboxExecutor.ShouldFail = false;
            var admin = fixture.Cluster.GrainFactory.GetGrain<IEdictDeadLetterAdmin>(
                id, "Edict.Core.Tests.Grains.CounterAggregate");
            await admin.RedriveAsync(entry.EntryId);

            Assert.Empty(await repo.ListAsync(id.ToString()));
        }
        finally
        {
            ControllableOutboxExecutor.ShouldFail = false;
        }
    }
}
