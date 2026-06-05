using MessagePack;

namespace Edict.Contracts.Commands;

/// <summary>
/// Opaque read-your-writes token echoed on <see cref="EdictCommandResult.Accepted"/>.
/// Wraps the chain-stable correlation id the framework minted for the Command, so
/// a consumer can later feed it to a Projection read that waits until the work
/// the Command set in motion is visible. A wrapper rather than a bare
/// <see cref="Guid"/> for type safety and so the surface can widen later without
/// a wire break. The public constructor lets a caller who supplied their own
/// correlation build a cursor without round-tripping <c>Accepted</c>.
/// </summary>
/// <param name="CorrelationId">The chain-stable correlation id this cursor names.</param>
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct EdictCursor(Guid CorrelationId);
