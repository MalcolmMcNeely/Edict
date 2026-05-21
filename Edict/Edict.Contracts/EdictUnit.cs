using Edict.Contracts.Persistence;

using MessagePack;

namespace Edict.Contracts;

/// <summary>
/// The empty payload for a stateless command handler or a payload-free
/// idempotent consumer. The non-generic <c>EdictCommandHandler</c> /
/// <c>EdictIdempotencyBase</c> shims close their generic base over this so
/// consumers never have to write <c>&lt;Unit&gt;</c> for the common no-state
/// case. <c>Edict.Contracts</c> is Orleans-runtime-free, so this
/// carries MessagePack attributes like every other contract type and is routed
/// through the same Orleans-MessagePack seam, never an Orleans
/// <c>[GenerateSerializer]</c>. Implements <see cref="IEdictPersistedState"/>
/// so the framework's stateless shims satisfy the persisted-state constraint
/// the generic bases impose.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct EdictUnit : IEdictPersistedState;
