using Edict.Contracts.Commands;

using FluentValidation;

namespace Edict.Core.Commands;

/// <summary>
/// Base for a Command Validator — a stateless DI service that gates an
/// <see cref="EdictCommand"/> before <c>HandleAsync</c> runs. Consumers derive
/// from this type and author rules with FluentValidation's <c>RuleFor</c> /
/// <c>When</c> / <c>WithErrorCode</c> / <c>WithMessage</c> DSL; the framework
/// resolves the validator from grain DI inside the generator-emitted
/// <c>DispatchAsync</c>, runs it in the same Orleans activation turn as the
/// handler, injects the current aggregate state via
/// <c>ValidationContext.RootContextData[SemanticConventions.Validation.GrainStateKey]</c>,
/// and maps failures to <see cref="EdictRejectionReason"/>s on
/// <see cref="EdictCommandResult.Rejected"/>. Failures are never thrown.
/// <para>
/// Validators are auto-discovered by <c>AddEdict(assembly)</c> through
/// FluentValidation's <c>AddValidatorsFromAssemblies</c>; no manual DI
/// registration is required. Name the class <c>{Name}CommandValidator</c> to
/// match the rest of the consumer role tree (<c>{Name}CommandHandler</c>,
/// <c>{Name}EventHandler</c>, <c>{Name}Saga</c>,
/// <c>{Name}ProjectionBuilder</c>).
/// </para>
/// </summary>
public abstract class EdictCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : EdictCommand;
