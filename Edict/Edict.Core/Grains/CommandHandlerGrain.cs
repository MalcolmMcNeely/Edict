using Edict.Contracts.Commands;
using Edict.Contracts.Results;
using Edict.Core.Validation;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Orleans;

namespace Edict.Core.Grains;

/// <summary>
/// Base for an aggregate grain. A Command is a direct grain call, so there is
/// deliberately no deduplication here (dedup is for at-least-once stream
/// delivery, which Commands never use — ADR 0004). The consumer writes a
/// <c>partial</c> grain with one strongly typed <c>Handle(TCommand)</c> per
/// command; the source generator emits the matching <see cref="Dispatch"/>
/// override that type-switches to those overloads, calling
/// <see cref="ValidateAndHandleAsync{TCommand}"/> per arm.
/// </summary>
public abstract class CommandHandlerGrain : Grain, IEdictCommandHandler
{
    /// <inheritdoc />
    public abstract Task<CommandResult> Dispatch(Command command);

    /// <summary>
    /// Resolves <see cref="IValidator{TCommand}"/> from grain DI, runs it with
    /// the current grain state in <c>ValidationContext.RootContextData</c>, and
    /// short-circuits to <see cref="CommandResult.Rejected"/> on failure.
    /// Returns the result of <paramref name="handle"/> when validation passes or
    /// no validator is registered. Called from the generated <c>Dispatch</c>.
    /// </summary>
    protected async Task<CommandResult> ValidateAndHandleAsync<TCommand>(
        TCommand command,
        Func<Task<CommandResult>> handle)
        where TCommand : Command
    {
        var validator = ServiceProvider.GetService<IValidator<TCommand>>();

        if (validator is not null)
        {
            var context = new ValidationContext<TCommand>(command);
            var state = GetValidationState();
            if (state is not null)
                context.RootContextData[EdictValidationKeys.GrainState] = state;

            var result = await validator.ValidateAsync(context);
            if (!result.IsValid)
            {
                return new CommandResult.Rejected(
                    result.Errors
                        .Select(static e => new RejectionReason(
                            e.ErrorCode ?? "validation_error",
                            e.ErrorMessage))
                        .ToArray());
            }
        }

        return await handle();
    }

    /// <summary>
    /// Override to expose the grain's current state to validators via
    /// <c>ValidationContext.RootContextData[<see cref="EdictValidationKeys.GrainState"/>]</c>.
    /// The default returns <c>null</c> (no state injected).
    /// </summary>
    protected virtual object? GetValidationState() => null;
}
