using System.Diagnostics;
using System.Security.Cryptography;

using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Postgres.Tests.Audit;

// Postgres-bespoke tamper-prevention conformance: the chain and payload tables are
// WORM by a BEFORE UPDATE/DELETE trigger that raises insufficient privilege even for
// the table owner, so the history cannot be rewritten in place. This is a Postgres
// property the Azure-Table chain does not have (it is tamper-evidence via the hash
// chain, not infra prevention), so it stays here rather than in the shared battery.
[Collection(PostgresAuditCollection.Name)]
public sealed class PostgresAuditWormTests(PostgresAuditFixture fixture)
{
    static string EntityType => typeof(CounterAggregate).FullName!;

    [Fact]
    public async Task WormTrigger_ShouldRefuseUpdateAndDelete()
    {
        // Arrange — the increment captures a C1 command record plus an E1 event record.
        var counterId = Guid.NewGuid();
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        var records = await WaitForRecordsAsync(counterId.ToString("N"), expectedCount: 2);
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
        var afterAttempts = await fixture.ReadEntityAsync(EntityType, counterId.ToString("N"));
        Assert.Equal(2, afterAttempts.Count);
    }

    [Fact]
    public async Task PayloadWormTrigger_ShouldRefuseUpdateAndDelete()
    {
        // Arrange — the increment captures records, each with a stored body.
        var counterId = Guid.NewGuid();
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        var records = await WaitForRecordsAsync(counterId.ToString("N"), expectedCount: 2);
        var recordId = records[0].RecordId;

        // Act + Assert — the BEFORE UPDATE/DELETE trigger raises insufficient
        // privilege (42501) even for the table owner.
        var updateState = await fixture.TryRawSqlAsync(
            $"UPDATE edict_audit_payload SET payload = '\\x00' WHERE record_id = '{recordId}';");
        Assert.Equal("42501", updateState);

        var deleteState = await fixture.TryRawSqlAsync(
            $"DELETE FROM edict_audit_payload WHERE record_id = '{recordId}';");
        Assert.Equal("42501", deleteState);

        // The body is still retrievable and still hashes to its record.
        var body = await fixture.GetPayloadAsync(recordId);
        Assert.Equal(records[0].PayloadHash, SHA256.HashData(body.Span));
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
