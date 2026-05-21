using Edict.Core.Idempotency;

namespace Edict.Core.Projections;

/// <summary>
/// Marker base for the two projection-builder roles. The dispatch seam
/// (<c>DispatchEventAsync</c>) now lives on the shared idempotency root
/// <see cref="EdictIdempotencyBase{TPayload}"/> so sagas share it too;
/// <c>EdictTableProjectionBuilder&lt;T&gt;</c> overrides it for
/// load-apply-writeback.
/// </summary>
public abstract class EdictProjectionBuilder : EdictIdempotencyBase;
