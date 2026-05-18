using System.Diagnostics;

using Edict.Contracts.Commands;

namespace Edict.Core.Sending;

/// <summary>
/// One generator-emitted routing entry: the concrete command type, the
/// aggregate grain interface that handles it, and a compiled accessor for the
/// command's <c>[EdictRouteKey]</c> Guid. Pure data — no Orleans dependency — so the
/// <see cref="CommandRouteResolver"/> stays unit-testable without a cluster.
/// </summary>
/// <param name="CommandType">The concrete <see cref="EdictCommand"/> subtype.</param>
/// <param name="GrainInterfaceType">
/// The generated per-aggregate marker interface — the typed routing token the
/// resolver returns. Note: Orleans never addresses by this type (Roslyn
/// generators cannot see each other's output, so Orleans' codegen never sees a
/// generated interface). The Orleans hop instead uses the real
/// <see cref="Grains.IEdictCommandHandler"/> plus <paramref name="GrainClassName"/>.
/// </param>
/// <param name="GrainClassName">
/// The aggregate grain class name, used to disambiguate the many grain classes
/// that share the <see cref="Grains.IEdictCommandHandler"/> interface.
/// </param>
/// <param name="RouteKeySelector">Reads the command's <c>[EdictRouteKey]</c> Guid.</param>
/// <param name="TagWriter">
/// Generator-emitted delegate that writes <c>[EdictTelemeterized]</c> property values
/// as OTEL tags on the active span. <see langword="null"/> when the command has
/// no annotated primitive properties.
/// </param>
public sealed record CommandRoute(
    Type CommandType,
    Type GrainInterfaceType,
    string GrainClassName,
    Func<EdictCommand, Guid> RouteKeySelector,
    Action<EdictCommand, Activity?>? TagWriter = null);
