using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Outbox;

namespace Edict.Postgres.Tests.Resilience;

// The conformance StateWriteFaultScenarios proves drop-dirty-activation +
// exactly-once-on-retry with a synthetic in-process exception. This pins the same
// behaviour against a real Postgres fault: an already-active grain whose commit
// write lands on a stopped backend, so EdictPostgresGrainStorage surfaces a genuine
// NpgsqlException (rethrown as EdictPostgresStorageException) mid-WriteStateAsync,
// and the uncommitted statement rolls back. The stream is the dumb MemoryStreams
// reference — only the store is faulted.
[Collection(PostgresResilienceCollection.Name)]
public sealed class PostgresRealWriteFaultTests(PostgresResilienceClusterFixture fixture)
{
    [Fact]
    public async Task CommandWriteFault_DropsDirtyActivation_AndAppliesExactlyOnceOnRetry_WhenPostgresStoppedMidWrite()
    {
        await fixture.EnsureRunningAsync();

        // Arrange — activate the grain and commit one increment while Postgres is
        // healthy, so the next command writes against an already-loaded activation
        // (a genuine mid-write, not a cold-read failure).
        var counterId = Guid.NewGuid();
        var probe = fixture.Cluster.GrainFactory.GetGrain<ICounterProbe>(counterId);
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        Assert.Equal(1, await probe.GetCountAsync());
        var initialActivationId = await probe.GetActivationIdAsync();

        // Act — stop Postgres, then send a second command. The handler mutates
        // in-memory state and the commit write faults against the stopped backend;
        // the uncommitted statement rolls back.
        await fixture.StopPostgresAsync();
        await Assert.ThrowsAnyAsync<Exception>(
            () => fixture.Sender.SendAsync(new IncrementCounterCommand(counterId)));

        // Recover the substrate; the framework drops the dirty activation, so a
        // reactivation reloads clean durable state. The changed activation id
        // confirms the drop.
        await fixture.StartPostgresAsync();
        await PostgresResilienceWaiters.WaitUntilAsync(
            async () => await probe.GetActivationIdAsync() != initialActivationId);
        Assert.NotEqual(initialActivationId, await probe.GetActivationIdAsync());

        // Act — retry against the clean activation.
        var retry = await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        // Assert — accepted and applied exactly once (count is 2, not 3 — no
        // double-apply over the half-applied faulted turn).
        Assert.IsType<EdictCommandResult.Accepted>(retry);
        Assert.Equal(2, await probe.GetCountAsync());
    }
}
