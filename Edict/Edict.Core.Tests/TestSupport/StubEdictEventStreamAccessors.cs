using System.Reflection;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Outbox;

namespace Edict.Core.Tests.TestSupport;

// Reflection-driven IEventStreamAccessors for unit tests that hand-wire
// ClaimCheckPolicy / DeadLetterPromoter outside DI. Production hosts use the
// generator-emitted dictionary lookup via EventStreamAccessors.
sealed class StubEdictEventStreamAccessors : IEventStreamAccessors
{
    public (string StreamName, Guid RouteKey) Resolve(EdictEvent edictEvent)
    {
        var type = edictEvent.GetType();
        var streamAttr = (EdictStreamAttribute?)Attribute.GetCustomAttribute(type, typeof(EdictStreamAttribute))
            ?? throw new InvalidOperationException($"Event {type.Name} is missing [EdictStream].");
        var routeKeyProp = Array.Find(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => Attribute.IsDefined(p, typeof(EdictRouteKeyAttribute)))
            ?? throw new InvalidOperationException($"Event {type.Name} is missing a [EdictRouteKey] Guid property.");
        return (streamAttr.Name, (Guid)routeKeyProp.GetValue(edictEvent)!);
    }
}
