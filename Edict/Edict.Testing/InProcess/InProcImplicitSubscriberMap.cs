using System.Collections.Concurrent;
using System.Reflection;

using Edict.Contracts.Events;
using Edict.Core.Idempotency;

using Orleans;
using Orleans.Streams;

namespace Edict.Testing.InProcess;

/// <summary>
/// Reflection-built table of <c>[ImplicitStreamSubscription]</c> predicates per
/// grain class. The Test Framework's in-process delivery
/// (<see cref="InProcPublishEventExecutor"/>) uses it to fan out an event
/// publish synchronously to every implicit subscriber — the substitute for the
/// Orleans memory-stream pulling agent that does not deliver to
/// referenced-assembly consumers in #53.
/// <para>
/// Only grain types deriving from <see cref="IEdictEventConsumer"/> (every
/// saga and projection builder) are considered. Other Orleans implicit
/// subscribers in the assembly (test fixtures, fakes) are out of scope because
/// they do not share the framework's dedup-guarded delivery seam.
/// </para>
/// </summary>
sealed class InProcImplicitSubscriberMap
{
    readonly IReadOnlyList<(Type GrainClass, IStreamNamespacePredicate Predicate)> _bindings;

    // Cache of (event CLR type → ordered list of subscribers) to avoid the
    // O(bindings) match per event published.
    readonly ConcurrentDictionary<Type, IReadOnlyList<Type>> _byEventType = new();

    InProcImplicitSubscriberMap(IReadOnlyList<(Type, IStreamNamespacePredicate)> bindings) =>
        _bindings = bindings;

    public static InProcImplicitSubscriberMap Build(Assembly consumerAssembly)
    {
        var consumerInterface = typeof(IEdictEventConsumer);
        var bindings = new List<(Type, IStreamNamespacePredicate)>();

        foreach (var type in consumerAssembly.GetTypes())
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

        return new InProcImplicitSubscriberMap(bindings);
    }

    /// <summary>Subscriber grain classes for the supplied event's stream.</summary>
    public IReadOnlyList<Type> SubscribersFor(EdictEvent evt) =>
        _byEventType.GetOrAdd(evt.GetType(), eventType =>
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
}
