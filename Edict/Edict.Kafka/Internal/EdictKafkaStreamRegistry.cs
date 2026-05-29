using System.Reflection;

using Edict.Contracts.Events;

namespace Edict.Kafka.Internal;

/// <summary>
/// Discovers every <see cref="EdictStreamAttribute"/> name in scope at silo
/// startup — the topology the per-stream Kafka topic layout rides on
/// (ADR-0028 §2). Stream names emit in sorted-ordinal order so two silos that
/// share an assembly set agree on the queue order Orleans' ring balancer
/// assigns. The string-list ctor is the production seam used by
/// <see cref="FromAppDomain"/>; tests construct it directly with hand-picked
/// names.
/// </summary>
sealed class EdictKafkaStreamRegistry
{
    public IReadOnlyList<string> StreamNames { get; }

    public EdictKafkaStreamRegistry(IEnumerable<string> streamNames)
    {
        StreamNames = streamNames
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static s => s, StringComparer.Ordinal)
            .ToArray();
    }

    public static EdictKafkaStreamRegistry FromAppDomain() =>
        new(Discover(AppDomain.CurrentDomain.GetAssemblies()));

    public static EdictKafkaStreamRegistry FromAssemblies(IEnumerable<Assembly> assemblies) =>
        new(Discover(assemblies));

    static IEnumerable<string> Discover(IEnumerable<Assembly> assemblies)
    {
        foreach (var asm in assemblies)
        {
            if (asm.IsDynamic)
            {
                continue;
            }

            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(static t => t is not null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (type is null || type.IsAbstract)
                {
                    continue;
                }
                if (!typeof(EdictEvent).IsAssignableFrom(type))
                {
                    continue;
                }
                var attr = type.GetCustomAttribute<EdictStreamAttribute>(inherit: false);
                if (attr is null)
                {
                    continue;
                }
                yield return attr.Name;
            }
        }
    }
}
