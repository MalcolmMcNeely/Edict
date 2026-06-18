using Edict.Contracts.Tenancy;

namespace Edict.Contracts.Audit;

/// <summary>
/// The consumer-facing read surface over the audit log. It reconstructs a whole
/// decision chain across grains (<see cref="ByCorrelationAsync"/>), a principal's
/// timeline (<see cref="ByPrincipalAsync"/>), and one aggregate's history
/// (<see cref="ByEntityAsync(string, string, CancellationToken)"/> for the full
/// chain, or the time-ranged overload for a window); it verifies an aggregate's
/// chain is unaltered (<see cref="VerifyEntityChainAsync"/>) and retrieves a
/// captured body either as raw bytes (<see cref="GetPayloadAsync"/>) or as the
/// concrete message the consumer authored (<see cref="GetMessageAsync"/>).
/// </summary>
public interface IEdictAuditRepository
{
    /// <summary>
    /// Every audit record for one aggregate, ordered by chain
    /// <see cref="EdictAuditRecord.Sequence"/>. A non-null <paramref name="tenant"/>
    /// narrows the result to that wall in the store; the default <see langword="null"/>
    /// returns the operator superset across every wall.
    /// </summary>
    Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, EdictTenantId? tenant = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// One aggregate's records whose <see cref="EdictAuditRecord.OccurredAt"/> falls
    /// in <c>[from, to)</c> (<paramref name="from"/> inclusive, <paramref name="to"/>
    /// exclusive), ordered by chain <see cref="EdictAuditRecord.Sequence"/>. A non-null
    /// <paramref name="tenant"/> narrows the result to that wall in the store; the
    /// default <see langword="null"/> returns the operator superset across every wall.
    /// </summary>
    Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to, EdictTenantId? tenant = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every record a correlation touched, across every grain it reached,
    /// reconstructed into one global order by <see cref="EdictAuditRecord.OccurredAt"/>
    /// (intent-time) so an auditor follows the transaction end to end. A non-null
    /// <paramref name="tenant"/> narrows the result to that wall in the store; the
    /// default <see langword="null"/> returns the operator superset across every wall.
    /// </summary>
    Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(Guid correlationId, EdictTenantId? tenant = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything a principal did over a time range: every record attributed to
    /// <paramref name="principal"/> whose <see cref="EdictAuditRecord.OccurredAt"/>
    /// falls in <c>[from, to)</c> (<paramref name="from"/> inclusive,
    /// <paramref name="to"/> exclusive), ordered by intent-time. A non-null
    /// <paramref name="tenant"/> narrows the result to that wall in the store; the
    /// default <see langword="null"/> returns the operator superset across every wall.
    /// </summary>
    Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, EdictTenantId? tenant = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies one aggregate's chain is unaltered: every record's hash
    /// recomputes from its stored content and links to its predecessor. Reports
    /// the first broken record so an auditor knows where the history diverged.
    /// </summary>
    Task<EdictAuditChainVerification> VerifyEntityChainAsync(string entityType, string entityKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The captured body behind a record, keyed by its <see cref="EdictAuditRecord.RecordId"/>:
    /// the serialized message bytes the record's <see cref="EdictAuditRecord.PayloadHash"/>
    /// was computed over, so an auditor sees <em>what</em> was decided, not merely
    /// that a decision happened. Retrieved by record id from the separate,
    /// separately-addressable payload store.
    /// </summary>
    Task<ReadOnlyMemory<byte>> GetPayloadAsync(Guid recordId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The captured body behind <paramref name="record"/> deserialized back to the
    /// concrete <c>EdictCommand</c> or <c>EdictEvent</c> the consumer authored,
    /// boxed because a correlation drill-down walks records of types the caller
    /// does not know in advance: pattern-match the returned object against the
    /// types you own. Takes the already-fetched record (its
    /// <see cref="EdictAuditRecord.Kind"/> selects the base, its
    /// <see cref="EdictAuditRecord.RecordId"/> locates the body) to save a
    /// re-read. Throws when the captured type cannot be resolved or the body cannot
    /// be deserialized in this process (a renamed, removed, or breaking-changed
    /// type, or its assembly not loaded); the bytes and hash stay available through
    /// <see cref="GetPayloadAsync"/> as the integrity anchor.
    /// </summary>
    Task<object> GetMessageAsync(EdictAuditRecord record, CancellationToken cancellationToken = default);
}
