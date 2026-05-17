using Edict.Contracts.Commands;
using Edict.Contracts.Results;

using Orleans;

namespace Edict.Core.Grains;

/// <summary>
/// The untyped marshalling surface every Edict aggregate grain exposes. The
/// source generator emits a per-aggregate interface that derives from this and
/// a <c>Dispatch</c> override that type-switches to the consumer's strongly
/// typed <c>Handle(TCommand)</c> overloads. Type safety lives on those
/// overloads, not here — no human authors or reads this interface (ADR 0004).
/// </summary>
public interface IEdictCommandHandler : IGrainWithGuidKey
{
    /// <summary>Routes a command to the matching <c>Handle</c> overload.</summary>
    Task<CommandResult> Dispatch(Command command);
}
