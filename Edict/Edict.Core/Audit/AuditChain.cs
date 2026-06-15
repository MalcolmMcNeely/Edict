using System.Collections.Immutable;
using System.ComponentModel;

namespace Edict.Core.Audit;

/// <summary>
/// The per-aggregate audit slot on the grain envelope: the running chain
/// <see cref="Head"/> (<c>prev_hash</c>) and sequence, plus the records staged in
/// this aggregate's commit but not yet drained to the WORM store. Null for every
/// grain until its first audited decision, so a non-audited (or auditing-off)
/// grain pays one empty reference. Persisted state, so a frozen
/// <see cref="OrleansAliasAttribute"/> survives a class rename; records are held
/// as serialized bytes like <c>OutboxEntry</c> rather than typed instances so the
/// envelope's generated serializer never has to reach into a contract type.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[GenerateSerializer]
[Alias("AuditChain")]
public sealed record AuditChain
{
    /// <summary>The latest chain hash, the <c>prev_hash</c> the next record folds in; the genesis link before the first record.</summary>
    [Id(0)]
    public byte[] Head { get; init; } = HashChain.Genesis;

    /// <summary>The sequence the next captured record takes; the chain's length so far.</summary>
    [Id(1)]
    public long NextSequence { get; init; }

    /// <summary>Records committed with the action but not yet drained to the store, in capture order.</summary>
    [Id(2)]
    public ImmutableList<AuditChainEntry> Pending { get; init; } = ImmutableList<AuditChainEntry>.Empty;

    /// <summary>
    /// Advances the chain by one sealed record: the new head is the record's own
    /// hash, the sequence ticks forward, and the serialized record joins the
    /// pending tail.
    /// </summary>
    public AuditChain Append(byte[] head, AuditChainEntry entry) =>
        this with { Head = head, NextSequence = NextSequence + 1, Pending = Pending.Add(entry) };

    /// <summary>Drops a record after a successful drain. Total: a no-op when no entry matches.</summary>
    public AuditChain Ack(Guid recordId)
    {
        var index = Pending.FindIndex(entry => entry.RecordId == recordId);
        return index < 0 ? this : this with { Pending = Pending.RemoveAt(index) };
    }
}
