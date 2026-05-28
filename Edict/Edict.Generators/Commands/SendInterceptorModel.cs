namespace Edict.Generators.Commands;

/// <summary>
/// One concrete-typed <c>IEdictSender.Send</c> call site discovered in the
/// consumer compilation. Captured as the
/// <see cref="Microsoft.CodeAnalysis.CSharp.InterceptableLocation"/>'s opaque
/// version/data pair (record-equality-safe) and a human-readable display
/// location for the generated comment.
/// </summary>
internal sealed record SendInvocationModel(
    string CommandFqn,
    int LocationVersion,
    string LocationData,
    string DisplayLocation);

/// <summary>
/// One concrete-typed <c>Raise</c> call site on an
/// <see cref="Edict.Generators.EdictWellKnownNames.EdictCommandHandlerFqn"/>
/// derivative. Used by the per-event Raise interceptor stub.
/// </summary>
internal sealed record RaiseInvocationModel(
    string EventFqn,
    int LocationVersion,
    string LocationData,
    string DisplayLocation);

/// <summary>
/// One concrete-typed <c>Dispatch</c> call site on an
/// <see cref="Edict.Generators.EdictWellKnownNames.EdictSagaFqn"/>
/// derivative. Used by the per-command Saga Dispatch interceptor stub.
/// </summary>
internal sealed record DispatchInvocationModel(
    string CommandFqn,
    int LocationVersion,
    string LocationData,
    string DisplayLocation);
