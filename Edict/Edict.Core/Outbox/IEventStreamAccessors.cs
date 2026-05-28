using Edict.Contracts.Events;

namespace Edict.Core.Outbox;

/// <summary>
/// Resolves the <see cref="EdictEvent"/>'s domain stream and route key without
/// per-publish reflection. The concrete implementation is generator-fed: every
/// concrete <see cref="EdictEvent"/> tagged <c>[EdictStream]</c> with a single
/// <c>[EdictRouteKey] Guid</c> contributes one map entry at startup.
/// </summary>
public interface IEventStreamAccessors
{
    /// <summary>
    /// Returns the domain stream name and route-key Guid for
    /// <paramref name="evt"/>.
    /// </summary>
    (string StreamName, Guid RouteKey) Resolve(EdictEvent evt);
}
