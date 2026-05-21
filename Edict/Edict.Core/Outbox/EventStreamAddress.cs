using System.Reflection;

using Edict.Contracts;
using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Edict.Core.Outbox;

static class EventStreamAddress
{
    public static (string StreamName, Guid RouteKey) Resolve(EdictEvent evt)
    {
        var type = evt.GetType();

        var streamAttr = (EdictStreamAttribute?)Attribute.GetCustomAttribute(type, typeof(EdictStreamAttribute))
            ?? throw new InvalidOperationException($"Event {type.Name} is missing [EdictStream] — every concrete event must declare its domain stream.");

        var routeKeyProp = Array.Find(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => Attribute.IsDefined(p, typeof(EdictRouteKeyAttribute)))
            ?? throw new InvalidOperationException($"Event {type.Name} is missing a [EdictRouteKey] Guid property.");

        return (streamAttr.Name, (Guid)routeKeyProp.GetValue(evt)!);
    }
}
