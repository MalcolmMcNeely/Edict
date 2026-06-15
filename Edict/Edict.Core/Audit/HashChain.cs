using System.Security.Cryptography;

using Edict.Contracts.Audit;

namespace Edict.Core.Audit;

// The per-aggregate tamper-evidence primitive: a hash chain whose every link
// folds the previous link into the new record's bytes, so altering any record
// after the fact breaks every link from that point on. Pure and Orleans-free —
// the grain holds the running head in its state and verification re-walks stored
// records, but neither needs the runtime to compute a hash.
static class HashChain
{
    // The link before the first record: 32 zero bytes, matching the SHA-256
    // digest width so a genesis link and a real link are indistinguishable in
    // shape and the first Advance has something to fold in.
    public static readonly byte[] Genesis = new byte[32];

    // Folds content into the chain: SHA-256 of the previous link followed by the
    // record's canonical bytes. Including the previous hash is what links record
    // N to N-1, so a removed or reordered record fails verification.
    public static byte[] Advance(byte[] previousHash, ReadOnlySpan<byte> content)
    {
        var buffer = new byte[previousHash.Length + content.Length];
        previousHash.CopyTo(buffer.AsSpan());
        content.CopyTo(buffer.AsSpan(previousHash.Length));
        return SHA256.HashData(buffer);
    }

    // Re-walks a stored chain in sequence order. A record passes only when its
    // hash recomputes from its own stored content (catching a content edit) and
    // its previous-hash links to the record before it (catching a removal or
    // reorder). Reports the first record that fails so an auditor sees exactly
    // where the history diverged. Re-derives the hashed bytes from the stored
    // fields via the frozen canonical form, never by re-serializing — a
    // MessagePack version bump must not read as tampering.
    public static EdictAuditChainVerification Verify(IReadOnlyList<EdictAuditRecord> records)
    {
        var expectedPreviousHash = Genesis;
        foreach (var record in records)
        {
            var recomputed = Advance(record.PreviousHash, AuditRecordCanonicalForm.Encode(record));
            if (!record.PreviousHash.AsSpan().SequenceEqual(expectedPreviousHash)
                || !record.RecordHash.AsSpan().SequenceEqual(recomputed))
            {
                return EdictAuditChainVerification.BrokenAt(record.Sequence);
            }

            expectedPreviousHash = record.RecordHash;
        }

        return EdictAuditChainVerification.Intact();
    }
}
