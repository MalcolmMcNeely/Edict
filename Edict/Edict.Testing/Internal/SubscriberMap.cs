using System.Collections.Concurrent;
using System.Reflection;

using Edict.Contracts.Events;
using Edict.Core.EventHandler;
using Edict.Core.Idempotency;

using Orleans;
using Orleans.Streams;

namespace Edict.Testing.Internal;

/// <summary>
/// Reflection-built table of <c>[ImplicitStreamSubscription]</c> predicates per
/// grain class. The in-process executor uses it to fan out a publish
/// synchronously to every implicit subscriber, sidestepping the Orleans
/// memory-stream pulling agent which doesn't deliver to referenced-assembly
/// consumers. Only grain types deriving from <see cref="IEdictEventConsumer"/>
/// (every saga and projection builder) are considered.
/// </summary>
sealed class SubscriberMap
{
    readonly IReadOnlyList<(Type GrainClass, IStreamNamespacePredicate Predicate)> _bindings;

    readonly ConcurrentDictionary<Type, IReadOnlyList<Type>> _byEventType = new();

    SubscriberMap(IReadOnlyList<(Type, IStreamNamespacePredicate)> bindings) =>
        _bindings = bindings;

    public static SubscriberMap Build(Assembly consumerAssembly)
    {
        var consumerInterface = typeof(IEdictEventConsumer);
        var bindings = new List<(Type, IStreamNamespacePredicate)>();

        var assemblies = new[] { consumerAssembly, consumerInterface.Assembly }
            .Distinct();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || !consumerInterface.IsAssignableFrom(type))
                {
                    continue;
                }

                foreach (var attr in type.GetCustomAttributes<ImplicitStreamSubscriptionAttribute>(inherit: false))
                {
                    bindings.Add((type, attr.Predicate));
                }
            }
        }

        return new SubscriberMap(bindings);
    }

    /// <summary>Subscriber grain classes for the supplied event's stream.</summary>
    public IReadOnlyList<Type> SubscribersFor(EdictEvent edictEvent) =>
        _byEventType.GetOrAdd(edictEvent.GetType(), eventType =>
        {
            var attr = eventType.GetCustomAttribute<EdictStreamAttribute>(inherit: true);
            if (attr is null)
            {
                return Array.Empty<Type>();
            }

            var matched = new List<Type>();
            foreach (var (grainClass, predicate) in _bindings)
            {
                if (predicate.IsMatch(attr.Name))
                {
                    matched.Add(grainClass);
                }
            }
            return matched;
        });

    public static bool IsEventHandler(Type grainClass) =>
        typeof(EdictEventHandler).IsAssignableFrom(grainClass);
}
