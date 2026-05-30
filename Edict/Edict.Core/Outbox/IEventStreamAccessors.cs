using Edict.Contracts.Events;

namespace Edict.Core.Outbox;

/// <summary>
/// Resolves the <see cref="EdictEvent"/>'s domain stream and route key without
/// per-publish reflection. The concrete implementation is generator-fed: every
/// concrete <see cref="EdictEvent"/> tagged <c>[EdictStream]</c> with a single
/// <c>[EdictRouteKey] Guid</c> contributes one map entry at startup.
/// </summary>
internal interface IEventStreamAccessors
{
    /// <summary>
    /// Returns the domain stream name and route-key Guid for
    /// <paramref name="edictEvent"/>.
    /// </summary>
    (string StreamName, Guid RouteKey) Resolve(EdictEvent edictEvent);
}
