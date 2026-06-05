using System.ComponentModel;

using Edict.Contracts.Projections;

namespace Edict.Core.Projections;

/// <summary>
/// Grain-boundary carrier for a read that crossed the projection grain: the
/// payload (a single row as <see cref="object"/>, or the partition's rows) plus the
/// <see cref="EdictReadStatus"/> the in-grain cursor wait resolved. The
/// <c>IEdictProjectionReader&lt;TRow&gt;</c> facade casts the payload back to the
/// typed row and re-wraps it as the consumer-facing
/// <see cref="EdictProjectionRead{TRow}"/> / <see cref="EdictProjectionPartitionRead{TRow}"/>.
/// Status must cross the boundary because only the grain knows whether the wait
/// reached the cursor or timed out.
/// <para>
/// Public because it is returned from the public <c>IEdictProjectionBuilder</c>
/// grain interface; not consumer-typed, so it is unprefixed and hidden from
/// consumer IntelliSense.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[GenerateSerializer]
public readonly record struct ProjectionReadResult<TPayload>(
    [property: Id(0)] TPayload Payload,
    [property: Id(1)] EdictReadStatus Status);
