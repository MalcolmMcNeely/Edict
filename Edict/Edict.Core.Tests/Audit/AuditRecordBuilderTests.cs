using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Core.Audit;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Audit;

public sealed class AuditRecordBuilderTests
{
    static readonly DateTimeOffset OccurredAt = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

    static EdictAuditRecord AcceptedContent() => new()
    {
        RecordId = new Guid("11111111-1111-1111-1111-111111111111"),
        Kind = EdictAuditKind.Command,
        Outcome = EdictAuditOutcome.Accepted,
        Principal = EdictPrincipal.Of("alice"),
        CorrelationId = new Guid("22222222-2222-2222-2222-222222222222"),
        EntityType = "Sample.Domain.OrderCommandHandler",
        EntityKey = "order-1",
        MessageType = "Sample.Contracts.PlaceOrderCommand",
        OccurredAt = OccurredAt,
        PayloadHash = [1, 2, 3, 4],
        PayloadReference = new Guid("33333333-3333-3333-3333-333333333333"),
    };

    [Fact]
    public Task Seal_ShouldStampSequenceAndChainHashes_OntoAcceptedRecord()
    {
        var sealedRecord = AuditRecordBuilder.Seal(AcceptedContent(), HashChain.Genesis, sequence: 0);

        return Verify(sealedRecord);
    }

    [Fact]
    public Task Seal_ShouldCarryRejectionReasons_OnRejectedRecord()
    {
        var content = AcceptedContent() with
        {
            Outcome = EdictAuditOutcome.Rejected,
            RejectionReasons = [new EdictRejectionReason("insufficient_funds", "Balance too low")],
        };

        var sealedRecord = AuditRecordBuilder.Seal(content, HashChain.Genesis, sequence: 0);

        return Verify(sealedRecord);
    }

    [Fact]
    public void Seal_ShouldLinkEachRecordToItsPredecessor()
    {
        var first = AuditRecordBuilder.Seal(AcceptedContent(), HashChain.Genesis, sequence: 0);
        var second = AuditRecordBuilder.Seal(
            AcceptedContent() with { RecordId = new Guid("44444444-4444-4444-4444-444444444444") },
            first.RecordHash,
            sequence: 1);

        Assert.Equal(HashChain.Genesis, first.PreviousHash);
        Assert.Equal(first.RecordHash, second.PreviousHash);
        Assert.NotEqual(first.RecordHash, second.RecordHash);
    }
}
