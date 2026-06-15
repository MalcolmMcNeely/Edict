using System.Diagnostics;

using Edict.Contracts.Audit;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Audit;

// Azure-bespoke tamper-evidence conformance. The Azure-Table chain has no infra
// tamper-prevention (no WORM trigger, unlike Postgres) until the deferred
// blob-sealing slice, so a privileged operator can rewrite a row. The guarantee Azure
// keeps is the hash chain: after a row is altered the chain no longer verifies and the
// verification names the first broken record. This is the evidence-not-prevention
// limitation made into a passing test.
[Collection(AzureAuditCollection.Name)]
public sealed class AzureAuditTamperEvidenceTests(AzureAuditFixture fixture)
{
    static string EntityType => typeof(CounterAggregate).FullName!;

    [Fact]
    public async Task AlteringAStoredRecord_BreaksChainVerification_AtThatRecord()
    {
        // Arrange — capture a C1 command record plus an E1 event record, intact.
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString();
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        var records = await WaitForRecordsAsync(entityKey, expectedCount: 2);

        var before = await fixture.VerifyChainAsync(EntityType, entityKey);
        Assert.True(before.IsIntact);

        // Act — rewrite the first record under a tampered principal, the mutation the
        // Postgres WORM trigger refuses but the Azure-Table chain permits.
        await fixture.TamperEntityRecordPrincipalAsync(EntityType, entityKey, records[0].RecordId);

        // Assert — the chain no longer verifies and names the altered record.
        var after = await fixture.VerifyChainAsync(EntityType, entityKey);
        Assert.False(after.IsIntact);
        Assert.Equal(records[0].Sequence, after.BrokenAtSequence);
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
