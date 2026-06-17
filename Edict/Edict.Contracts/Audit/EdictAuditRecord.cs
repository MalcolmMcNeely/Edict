using Edict.Contracts.Commands;
using Edict.Contracts.Tenancy;

using MessagePack;

namespace Edict.Contracts.Audit;

/// <summary>
/// An immutable, attributable statement that a decision happened. A Command
/// decision is captured at C1 (<c>EdictCommandHandler.ValidateAndHandleAsync</c>),
/// the only choke point that sees a rejection; each raised Event is captured at E1
/// (the outbox enqueue point where its <c>EventId</c> is minted), one record per
/// event. The record holds the attribution
/// (<see cref="Principal"/>), the causal spine (<see cref="CorrelationId"/>,
/// <see cref="RecordId"/>), the message type identity, the outcome, and a
/// <see cref="PayloadHash"/> of the body — never the body inline; the body is
/// fetched from a separate payload store by <see cref="RecordId"/>. It is made
/// tamper-evident by the
/// per-aggregate hash chain: <see cref="PreviousHash"/> links it to the prior
/// record on the same aggregate and <see cref="RecordHash"/> seals this one.
/// Read back through <see cref="IEdictAuditRepository"/>; never an event-sourcing
/// log (no replay, no rebuild).
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EdictAuditRecord
{
    /// <summary>Framework-assigned identity for this record; the chain link's own id.</summary>
    public Guid RecordId { get; init; }

    /// <summary>Whether this record captures a Command decision or an Event occurrence.</summary>
    public EdictAuditKind Kind { get; init; }

    /// <summary>How a Command decision resolved; <see langword="null"/> for an Event record.</summary>
    public EdictAuditOutcome? Outcome { get; init; }

    /// <summary>The structured reasons a Command was rejected; empty unless <see cref="Outcome"/> is <see cref="EdictAuditOutcome.Rejected"/>.</summary>
    public IReadOnlyList<EdictRejectionReason> RejectionReasons { get; init; } = [];

    /// <summary>The actor on whose authority the captured decision was made; <see langword="null"/> when auditing carried no principal.</summary>
    public EdictPrincipal? Principal { get; init; }

    /// <summary>The tenant whose data the captured decision belongs to, sibling to <see cref="Principal"/>; <see langword="null"/> for a public (tenant-less) decision.</summary>
    public EdictTenantId? Tenant { get; init; }

    /// <summary>Chain-stable correlation id of the conversation the captured decision belongs to.</summary>
    public Guid CorrelationId { get; init; }

    /// <summary>Full type name of the aggregate grain whose decision this is — the entity the chain belongs to.</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Key of the aggregate grain whose decision this is.</summary>
    public string EntityKey { get; init; } = string.Empty;

    /// <summary>Full type name of the captured Command (or Event) message.</summary>
    public string MessageType { get; init; } = string.Empty;

    /// <summary>When the decision was made, by the framework clock.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Monotonic position of this record within its aggregate's chain, starting at zero.</summary>
    public long Sequence { get; init; }

    /// <summary>The previous link in this aggregate's hash chain (<c>prev_hash</c>); the genesis link for the first record.</summary>
    public byte[] PreviousHash { get; init; } = [];

    /// <summary>This record's own chain hash (<c>this_hash</c>), folding <see cref="PreviousHash"/> into the record's canonical bytes.</summary>
    public byte[] RecordHash { get; init; } = [];

    /// <summary>Hash of the captured message body, so content tampering is detectable without storing the body in the chain.</summary>
    public byte[] PayloadHash { get; init; } = [];
}
