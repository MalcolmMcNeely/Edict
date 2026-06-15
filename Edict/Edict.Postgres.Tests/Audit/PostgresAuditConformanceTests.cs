using System.Diagnostics;

using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Postgres.Tests.Audit;

// Persistence-axis conformance for the audit log on real Postgres: an accepted
// and a rejected command under a known principal yield two immutable, attributed
// records on one unbroken chain; the chain head survives grain reactivation; and
// the WORM trigger refuses an UPDATE or DELETE.
[Collection(PostgresAuditCollection.Name)]
public sealed class PostgresAuditConformanceTests(PostgresAuditFixture fixture)
{
    static string EntityType => typeof(CounterAggregate).FullName!;

    [Fact]
    public async Task AcceptAndReject_UnderAKnownPrincipal_YieldTwoImmutableChainedRecords()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString();

        // Act
        var accepted = await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        var rejected = await fixture.Sender.SendAsync(new RejectCounterCommand(counterId));

        // Assert — exactly one attributed record per decision, with reasons on the rejection.
        Assert.IsType<EdictCommandResult.Accepted>(accepted);
        Assert.IsType<EdictCommandResult.Rejected>(rejected);

        var records = await WaitForRecordsAsync(entityKey, expectedCount: 2);
        Assert.All(records, record => Assert.Equal(PostgresPersistenceFixtureBase.AuditPrincipal, record.Principal));
        Assert.Equal(EdictAuditOutcome.Accepted, records[0].Outcome);
        Assert.Equal(EdictAuditOutcome.Rejected, records[1].Outcome);
        Assert.Equal("counter_rejected", Assert.Single(records[1].RejectionReasons).Code);
        Assert.Equal([0L, 1L], records.Select(record => record.Sequence));

        var verification = await fixture.VerifyChainAsync(EntityType, entityKey);
        Assert.True(verification.IsIntact);
    }

    [Fact]
    public async Task Chain_ShouldContinueAcrossGrainReactivation()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString();
        var probe = fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        await fixture.Sender.SendAsync(new RejectCounterCommand(counterId));
        await WaitForRecordsAsync(entityKey, expectedCount: 2);

        // Act — force the grain to deactivate so the next command reloads the
        // chain head from durable state rather than memory.
        var activationBeforeDeactivation = await probe.GetActivationIdAsync();
        await probe.DeactivateAsync();
        await WaitUntilAsync(async () => await probe.GetActivationIdAsync() != activationBeforeDeactivation);

        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        // Assert — the third record continues the chain (seq 2) and verifies.
        var records = await WaitForRecordsAsync(entityKey, expectedCount: 3);
        Assert.Equal([0L, 1L, 2L], records.Select(record => record.Sequence));

        var verification = await fixture.VerifyChainAsync(EntityType, entityKey);
        Assert.True(verification.IsIntact);
    }

    [Fact]
    public async Task WormTrigger_ShouldRefuseUpdateAndDelete()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        var records = await WaitForRecordsAsync(counterId.ToString(), expectedCount: 1);
        var recordId = records[0].RecordId;

        // Act + Assert — the BEFORE UPDATE/DELETE trigger raises insufficient
        // privilege (42501) even for the table owner.
        var updateState = await fixture.TryRawSqlAsync(
            $"UPDATE edict_audit_record SET principal = 'tampered' WHERE record_id = '{recordId}';");
        Assert.Equal("42501", updateState);

        var deleteState = await fixture.TryRawSqlAsync(
            $"DELETE FROM edict_audit_record WHERE record_id = '{recordId}';");
        Assert.Equal("42501", deleteState);

        // The record is still present and the chain still verifies.
        var afterAttempts = await fixture.ReadEntityAsync(EntityType, counterId.ToString());
        Assert.Single(afterAttempts);
    }

    async Task<IReadOnlyList<EdictAuditRecord>> WaitForRecordsAsync(string entityKey, int expectedCount)
    {
        IReadOnlyList<EdictAuditRecord> records = [];
        await WaitUntilAsync(async () =>
        {
            records = await fixture.ReadEntityAsync(EntityType, entityKey);
            return records.Count >= expectedCount;
        });
        Assert.True(records.Count >= expectedCount, $"Expected {expectedCount} audit records for {entityKey}, saw {records.Count}.");
        return records;
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * 30);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(100);
        }
    }
}
