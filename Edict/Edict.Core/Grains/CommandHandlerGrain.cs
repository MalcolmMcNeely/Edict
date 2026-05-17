using Edict.Contracts.Commands;
using Edict.Contracts.Results;

using Orleans;

namespace Edict.Core.Grains;

/// <summary>
/// Base for an aggregate grain. A Command is a direct grain call, so there is
/// deliberately no deduplication here (dedup is for at-least-once stream
/// delivery, which Commands never use — ADR 0004). The consumer writes a
/// <c>partial</c> grain with one strongly typed <c>Handle(TCommand)</c> per
/// command; the source generator emits the matching <see cref="Dispatch"/>
/// override that type-switches to those overloads.
/// </summary>
public abstract class CommandHandlerGrain : Grain, IEdictCommandHandler
{
    /// <inheritdoc />
    public abstract Task<CommandResult> Dispatch(Command command);
}
