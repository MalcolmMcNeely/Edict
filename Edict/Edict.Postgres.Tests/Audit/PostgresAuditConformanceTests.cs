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
    public async Task AcceptRaiseAndReject_UnderAKnownPrincipal_YieldImmutableChainedCommandAndEventRecords()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString();

        // Act — the accepted increment raises one event (one C1 command record plus
        // one E1 event record); the rejection raises nothing (one C1 command record).
        var accepted = await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        var rejected = await fixture.Sender.SendAsync(new RejectCounterCommand(counterId));

        // Assert — every record attributed to the known principal, on one chain.
        var cursor = Assert.IsType<EdictCommandResult.Accepted>(accepted).Cursor;
        Assert.IsType<EdictCommandResult.Rejected>(rejected);

        var records = await WaitForRecordsAsync(entityKey, expectedCount: 3);
        Assert.All(records, record => Assert.Equal(PostgresPersistenceFixtureBase.AuditPrincipal, record.Principal));
        Assert.Equal(
            [EdictAuditKind.Command, EdictAuditKind.Event, EdictAuditKind.Command],
            records.Select(record => record.Kind));
        Assert.Equal([0L, 1L, 2L], records.Select(record => record.Sequence));

        // The command decisions: accepted then rejected, with reasons on the rejection.
        var commandRecords = records.Where(record => record.Kind == EdictAuditKind.Command).ToArray();
        Assert.Equal(EdictAuditOutcome.Accepted, commandRecords[0].Outcome);
        Assert.Equal(EdictAuditOutcome.Rejected, commandRecords[1].Outcome);
        Assert.Equal("counter_rejected", Assert.Single(commandRecords[1].RejectionReasons).Code);

        // The E1 event record: present, attributed, carrying the inherited
        // correlation, with no command outcome.
        var eventRecord = Assert.Single(records, record => record.Kind == EdictAuditKind.Event);
        Assert.Equal(cursor.CorrelationId, eventRecord.CorrelationId);
        Assert.Null(eventRecord.Outcome);
        Assert.Equal(typeof(CounterIncrementedEvent).FullName, eventRecord.MessageType);

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

        // Increment (C1 + E1) then reject (C1) — three records before deactivation.
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        await fixture.Sender.SendAsync(new RejectCounterCommand(counterId));
        await WaitForRecordsAsync(entityKey, expectedCount: 3);

        // Act — force the grain to deactivate so the next command reloads the
        // chain head from durable state rather than memory.
        var activationBeforeDeactivation = await probe.GetActivationIdAsync();
        await probe.DeactivateAsync();
        await WaitUntilAsync(async () => await probe.GetActivationIdAsync() != activationBeforeDeactivation);

        // A second increment after reactivation adds another C1 + E1.
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        // Assert — the post-reactivation records continue the chain (seq 3, 4) and verify.
        var records = await WaitForRecordsAsync(entityKey, expectedCount: 5);
        Assert.Equal([0L, 1L, 2L, 3L, 4L], records.Select(record => record.Sequence));

        var verification = await fixture.VerifyChainAsync(EntityType, entityKey);
        Assert.True(verification.IsIntact);
    }

    [Fact]
    public async Task WormTrigger_ShouldRefuseUpdateAndDelete()
    {
        // Arrange — the increment captures a C1 command record plus an E1 event record.
        var counterId = Guid.NewGuid();
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        var records = await WaitForRecordsAsync(counterId.ToString(), expectedCount: 2);
        var recordId = records[0].RecordId;

        // Act + Assert — the BEFORE UPDATE/DELETE trigger raises insufficient
        // privilege (42501) even for the table owner.
        var updateState = await fixture.TryRawSqlAsync(
            $"UPDATE edict_audit_record SET principal = 'tampered' WHERE record_id = '{recordId}';");
        Assert.Equal("42501", updateState);

        var deleteState = await fixture.TryRawSqlAsync(
            $"DELETE FROM edict_audit_record WHERE record_id = '{recordId}';");
        Assert.Equal("42501", deleteState);

        // Both records are still present and the chain still verifies.
        var afterAttempts = await fixture.ReadEntityAsync(EntityType, counterId.ToString());
        Assert.Equal(2, afterAttempts.Count);
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
