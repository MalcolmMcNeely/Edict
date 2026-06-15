using Edict.Contracts.Audit;

namespace Edict.Core.Audit;

// Closes a content-filled audit record into a chain link: stamps its position and
// the two hash fields. The grain fills the decision (kind, outcome, reasons,
// attribution, spine, payload hash); this seals it against the running chain head
// the grain holds in its state. Pure — no Orleans, no I/O — so the hash logic is
// unit-testable in isolation.
static class AuditRecordBuilder
{
    public static EdictAuditRecord Seal(EdictAuditRecord content, byte[] previousHash, long sequence)
    {
        var staged = content with { Sequence = sequence, PreviousHash = previousHash };
        var recordHash = HashChain.Advance(previousHash, AuditRecordCanonicalForm.Encode(staged));
        return staged with { RecordHash = recordHash };
    }
}
