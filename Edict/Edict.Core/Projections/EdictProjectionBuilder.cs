using Edict.Contracts.Persistence;

namespace Edict.Core.Projections;

/// <summary>
/// Projection builder whose read model lives in the grain's own durable state and
/// commits inline with the dedup ring in one write. No external store, no outbox
/// effect, and read-your-writes resolves the instant the write lands. Structurally
/// a saga without <c>Dispatch</c>: the consumer mutates <see cref="Projection"/>
/// inside a <c>HandleAsync</c> and the payload write rides the dedup-ring commit
/// atomically. Best for a small, hot, per-aggregate read model; the cost is that
/// the read model inflates grain activation, which is the consumer's call to make.
/// For a large or unbounded read model, use
/// <see cref="EdictListProjectionBuilder{TListProjection}"/> instead.
/// </summary>
public abstract class EdictProjectionBuilder<TProjection> : EdictProjectionBuilderBase<TProjection>
    where TProjection : IEdictPersistedState, new()
{
    /// <summary>
    /// The in-grain read model. The consumer mutates this inside a
    /// <c>HandleAsync</c>; it is the payload slot of the persisted envelope,
    /// committed atomically with the dedup ring. Mirrors a saga's <c>Progress</c>.
    /// </summary>
    protected TProjection Projection => State.Payload;

    /// <summary>Serves a consumer read of the whole in-grain projection. The
    /// read-your-writes wait runs on the base before this returns.</summary>
    protected override Task<object?> ReadAsync() => Task.FromResult<object?>(Projection);
}
