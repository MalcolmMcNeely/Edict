using Edict.Core.Idempotency;

namespace Edict.Core.Projections;

/// <summary>
/// Marker base for the projection-builder species. The dispatch seam
/// (<c>DispatchEventAsync</c>) lives on the shared idempotency root
/// <see cref="EdictIdempotencyBase{TPayload}"/> so sagas share it too;
/// <see cref="EdictListProjectionBuilder{TRow}"/> overrides it for
/// load-apply-writeback. This root also carries the grain read surface
/// (<see cref="IEdictProjectionBuilder"/>) so every concrete species answers
/// reads through the same mechanism; the default throws because a projection
/// with no row store (one that exposes its state through a bespoke grain
/// interface) has nothing for the read facade to return.
/// </summary>
public abstract class EdictProjectionBuilder : EdictIdempotencyBase, IEdictProjectionBuilder
{
    /// <inheritdoc />
    public virtual Task<object?> EdictReadRowAsync(string rowKey) =>
        throw new EdictUnsupportedProjectionReadException(GetType());

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<object>> EdictReadPartitionAsync() =>
        throw new EdictUnsupportedProjectionReadException(GetType());
}
