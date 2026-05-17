using Edict.Contracts.Commands;

namespace Edict.Core.Sending;

/// <summary>
/// Thrown when a <see cref="Command"/> has no generator-emitted route — i.e.
/// no aggregate grain declares a <c>Handle</c> overload for its type. This is
/// a wiring fault (missing <c>Handle</c> or missing <c>AddEdict()</c>), not a
/// business rejection, so it throws rather than returning a
/// <see cref="Contracts.Results.CommandResult.Rejected"/>.
/// </summary>
public sealed class UnroutableCommandException(Type commandType)
    : InvalidOperationException(
        $"No aggregate grain handles command '{commandType.FullName}'. " +
        $"Declare a Handle({commandType.Name}) overload on a CommandHandlerGrain " +
        "and register it with AddEdict().")
{
    /// <summary>The command type that could not be routed.</summary>
    public Type CommandType { get; } = commandType;
}
