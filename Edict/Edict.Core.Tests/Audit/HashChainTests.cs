using System.Security.Cryptography;

using Edict.Contracts.Audit;
using Edict.Core.Audit;

using Xunit;

namespace Edict.Core.Tests.Audit;

public sealed class HashChainTests
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
        var verification = HashChain.Verify(ThreeRecordChain());

        Assert.True(verification.IsIntact);
        Assert.Null(verification.BrokenAtSequence);
    }

    [Fact]
    public void Verify_ShouldReportBrokenAtRecord_WhenAStoredFieldIsAltered()
    {
        var records = ThreeRecordChain().ToList();

        // Tamper with the middle record's stored content, leaving its hash columns
        // untouched the way an attacker editing the row directly would.
        records[1] = records[1] with { EntityKey = "order-999" };

        var verification = HashChain.Verify(records);

        Assert.False(verification.IsIntact);
        Assert.Equal(1, verification.BrokenAtSequence);
    }

    [Fact]
    public void Advance_ShouldHashPreviousHashThenContent_Deterministically()
    {
        // Arrange
        var content = "decision-one"u8.ToArray();
        var expected = SHA256.HashData([.. HashChain.Genesis, .. content]);

        // Act
        var first = HashChain.Advance(HashChain.Genesis, content);
        var second = HashChain.Advance(HashChain.Genesis, content);

        // Assert
        Assert.Equal(expected, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Advance_ShouldProduceDistinctLinks_WhenContentDiffers()
    {
        // Act
        var first = HashChain.Advance(HashChain.Genesis, "decision-one"u8.ToArray());
        var second = HashChain.Advance(first, "decision-two"u8.ToArray());

        // Assert
        Assert.NotEqual(first, second);
    }
}
