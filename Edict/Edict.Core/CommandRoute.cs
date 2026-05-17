using Edict.Abstractions;

namespace Edict.Core;

/// <summary>
/// One generator-emitted routing entry: the concrete command type, the
/// aggregate grain interface that handles it, and a compiled accessor for the
/// command's <c>[RouteKey]</c> Guid. Pure data — no Orleans dependency — so the
/// <see cref="CommandRouteResolver"/> stays unit-testable without a cluster.
/// </summary>
/// <param name="CommandType">The concrete <see cref="Command"/> subtype.</param>
/// <param name="GrainInterfaceType">
/// The generated per-aggregate marker interface — the typed routing token the
/// resolver returns. Note: Orleans never addresses by this type (Roslyn
/// generators cannot see each other's output, so Orleans' codegen never sees a
/// generated interface). The Orleans hop instead uses the real
/// <see cref="IEdictCommandHandler"/> plus <paramref name="GrainClassName"/>.
/// </param>
/// <param name="GrainClassName">
/// The aggregate grain class name, used to disambiguate the many grain classes
/// that share the <see cref="IEdictCommandHandler"/> interface.
/// </param>
/// <param name="RouteKeySelector">Reads the command's <c>[RouteKey]</c> Guid.</param>
public sealed record CommandRoute(
    Type CommandType,
    Type GrainInterfaceType,
    string GrainClassName,
    Func<Command, Guid> RouteKeySelector);
