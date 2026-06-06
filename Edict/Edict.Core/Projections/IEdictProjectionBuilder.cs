using Edict.Core.Idempotency;

using Orleans.Concurrency;

namespace Edict.Core.Projections;

/// <summary>
/// Grain-interface seam every projection builder shares. Reads route through the
/// grain (not the backing store directly) so a future read-your-writes wait can
/// be hosted on the activation that owns the rows. The read methods are
/// <see cref="AlwaysInterleaveAttribute"/> because the activation is
/// single-threaded and turn-based: a parked read that ran as a normal turn would
/// block the very stream-delivery turn that satisfies it. They live here, on the
/// hand-written interface the client proxy dispatches through, because Orleans
/// honours the attribute on the interface method, and because Orleans codegen
/// runs before Edict's generator, so a generator-emitted grain interface would
/// get no client proxy (the same constraint that makes <c>IEdictSaga</c>
/// hand-written).
/// <para>
/// Read models cross the grain boundary as <see cref="object"/> so the
/// non-generic interface can serve every species; the
/// <c>IEdictListProjectionReader&lt;TListProjection&gt;</c> and
/// <c>IEdictProjectionReader&lt;TProjection&gt;</c> facades cast back to the typed
/// read model.
/// </para>
/// </summary>
public interface IEdictProjectionBuilder : IEdictEventConsumer
{
    /// <summary>
    /// Point-gets the row at <paramref name="rowKey"/>, optionally waiting until the
    /// correlation named by <paramref name="afterCorrelationId"/> is visible. A
    /// null correlation answers immediately (the poll path); otherwise the grain
    /// parks until the correlation is processed or <paramref name="timeout"/>
    /// elapses (a null timeout falls back to the bounded
    /// <c>EdictOptions.ProjectionReadTimeout</c>). The
    /// <see cref="ProjectionReadResult{TPayload}"/> carries the read status because
    /// only the grain knows whether the wait reached the cursor or timed out.
    /// </summary>
    [AlwaysInterleave]
    Task<ProjectionReadResult<object?>> EdictReadRowAsync(string rowKey, Guid? afterCorrelationId, TimeSpan? timeout);

    /// <summary>
    /// Returns every row in this projection's partition, with the same optional
    /// read-your-writes wait as <see cref="EdictReadRowAsync"/>.
    /// </summary>
    [AlwaysInterleave]
    Task<ProjectionReadResult<IReadOnlyList<object>>> EdictReadPartitionAsync(Guid? afterCorrelationId, TimeSpan? timeout);

    /// <summary>
    /// Returns the whole in-grain projection payload, with the same optional
    /// read-your-writes wait as <see cref="EdictReadRowAsync"/>. Served by the
    /// in-grain State species; the List species throws
    /// <see cref="EdictUnsupportedProjectionReadException"/> for this read.
    /// </summary>
    [AlwaysInterleave]
    Task<ProjectionReadResult<object?>> EdictReadAsync(Guid? afterCorrelationId, TimeSpan? timeout);
}
