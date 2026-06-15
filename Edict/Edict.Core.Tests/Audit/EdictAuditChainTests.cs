using Edict.Contracts.Audit;
using Edict.Core.Audit;

using Xunit;

namespace Edict.Core.Tests.Audit;

// Pins the public verifier consumers call to check a chain they already hold in
// memory: the seam the Sample audit page leans on to show a deliberately
// tampered copy reported broken without ever mutating the write-once store.
public sealed class EdictAuditChainTests
{
    static IReadOnlyList<EdictAuditRecord> ThreeRecordChain()
    {
        var records = new List<EdictAuditRecord>();
        var previousHash = HashChain.Genesis;
        for (var sequence = 0; sequence < 3; sequence++)
        {
            var sealedRecord = AuditRecordBuilder.Seal(
                new EdictAuditRecord
                {
                    RecordId = Guid.NewGuid(),
                    Kind = EdictAuditKind.Command,
                    Outcome = EdictAuditOutcome.Accepted,
                    EntityType = "OrderCommandHandler",
                    EntityKey = "order-1",
                    MessageType = "PlaceOrderCommand",
                    OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(sequence),
                },
                previousHash,
                sequence);
            records.Add(sealedRecord);
            previousHash = sealedRecord.RecordHash;
        }
        return records;
    }

    [Fact]
    public void Verify_ShouldReportIntact_ForAnUntamperedChain()
    {
        var verification = EdictAuditChain.Verify(ThreeRecordChain());

        Assert.True(verification.IsIntact);
        Assert.Null(verification.BrokenAtSequence);
    }

    [Fact]
    public void Verify_ShouldReportBrokenAtRecord_WhenACopyIsTampered()
    {
        var records = ThreeRecordChain().ToList();

        // Rewrite the middle record's content in the in-memory copy, leaving its
        // hash columns untouched the way an edited row would arrive.
        records[1] = records[1] with { EntityKey = "order-999" };

        var verification = EdictAuditChain.Verify(records);

        Assert.False(verification.IsIntact);
        Assert.Equal(1, verification.BrokenAtSequence);
    }
}
