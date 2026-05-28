using Edict.Contracts.Events;

namespace Edict.Contracts.Routing;

/// <summary>
/// One generator-emitted routing entry: the domain stream name a concrete
/// <see cref="EdictEvent"/> rides on, paired with a compiled accessor for its
/// <c>[EdictRouteKey]</c> Guid. Pure data — no Orleans, no DI — so the
/// resolver stays unit-testable and the registrar can be emitted into a
/// contracts-only assembly without dragging Edict.Core in. Mirrors the
/// <c>CommandRoute</c> shape on the command side.
/// </summary>
/// <param name="StreamName">The domain stream named by <c>[EdictStream]</c>.</param>
/// <param name="RouteKeyGetter">Reads the event's <c>[EdictRouteKey]</c> Guid.</param>
public sealed record EdictEventStreamAccessor(
    string StreamName,
    Func<EdictEvent, Guid> RouteKeyGetter);
