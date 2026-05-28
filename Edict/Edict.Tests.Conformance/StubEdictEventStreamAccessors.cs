using System.Reflection;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Outbox;

namespace Edict.Tests.Conformance;

/// <summary>
/// Reflection-driven <see cref="IEventStreamAccessors"/> for hand-wired tests
/// in conformance / provider suites that bypass DI. Production hosts use the
/// generator-emitted dictionary lookup via <c>EventStreamAccessors</c>.
/// </summary>
public sealed class StubEdictEventStreamAccessors : IEventStreamAccessors
{
    public (string StreamName, Guid RouteKey) Resolve(EdictEvent evt)
    {
        var type = evt.GetType();
        var streamAttr = (EdictStreamAttribute?)Attribute.GetCustomAttribute(type, typeof(EdictStreamAttribute))
            ?? throw new InvalidOperationException($"Event {type.Name} is missing [EdictStream].");
        var routeKeyProp = Array.Find(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => Attribute.IsDefined(p, typeof(EdictRouteKeyAttribute)))
            ?? throw new InvalidOperationException($"Event {type.Name} is missing a [EdictRouteKey] Guid property.");
        return (streamAttr.Name, (Guid)routeKeyProp.GetValue(evt)!);
    }
}
