using System.Reflection;

using Xunit;

namespace Edict.Architecture.Tests;

// Drift guard for the axis-conformance batteries: every axis-provider binding
// project must bind every scenario in its battery — full within-battery symmetry,
// no opt-out list. A new scenario added to a battery, or a provider that quietly
// stops binding one, turns this red and names the gap. Pairs with
// EdictTestsConformance_ShouldNotDependOnAnyProviderSdkPackages, which keeps the
// batteries and their dumb references SDK-free.
public class ConformanceBindingCompletenessTests
{
    const string StreamingNamespace = "Edict.Tests.Conformance.Streaming";
    const string PersistenceNamespace = "Edict.Tests.Conformance.Persistence";

    static readonly Assembly ConformanceAssembly =
        typeof(Edict.Tests.Conformance.Streaming.StreamingConformanceFixture).Assembly;

    // The two axis-provider binding assemblies per battery.
    static readonly Assembly[] StreamingProviders =
    [
        typeof(Edict.Azure.Streaming.Tests.AqsStreamingFixture).Assembly,
        typeof(Edict.Kafka.Tests.KafkaStreamingFixture).Assembly,
    ];

    static readonly Assembly[] PersistenceProviders =
    [
        typeof(Edict.Azure.Persistence.Tests.AzurePersistenceFixture).Assembly,
        typeof(Edict.Postgres.Persistence.Tests.PostgresPersistenceFixture).Assembly,
    ];

    [Fact]
    public void EveryStreamingProvider_BindsEveryStreamingBatteryScenario()
    {
        AssertBatteryFullyBound(StreamingNamespace, StreamingProviders);
    }

    [Fact]
    public void EveryPersistenceProvider_BindsEveryPersistenceBatteryScenario()
    {
        AssertBatteryFullyBound(PersistenceNamespace, PersistenceProviders);
    }

    static void AssertBatteryFullyBound(string batteryNamespace, Assembly[] providers)
    {
        var batteryDefinitions = AllBatteryScenarioDefinitions();
        var batteryScenarios = ScenarioDefinitions(batteryNamespace)
            .Select(CleanName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(batteryScenarios.Count > 0, $"No battery scenarios discovered in namespace {batteryNamespace}.");

        var providerBindings = providers.ToDictionary(
            provider => provider.GetName().Name!,
            provider => BoundScenarioNames(provider, batteryDefinitions),
            StringComparer.Ordinal);

        var gaps = ConformanceBindingCompleteness.FindBindingGaps(batteryScenarios, providerBindings);

        Assert.True(gaps.Count == 0, "Conformance binding gaps:\n" + string.Join("\n", gaps));
    }

    static HashSet<Type> AllBatteryScenarioDefinitions() =>
        ScenarioDefinitions(StreamingNamespace)
            .Concat(ScenarioDefinitions(PersistenceNamespace))
            .ToHashSet();

    static IReadOnlyList<Type> ScenarioDefinitions(string batteryNamespace) =>
        SafeGetTypes(ConformanceAssembly)
            .Where(type => type.Namespace == batteryNamespace
                && type.IsAbstract
                && type.IsGenericTypeDefinition
                && type.Name.Contains("Scenarios", StringComparison.Ordinal))
            .ToList();

    // Every battery scenario a provider's sealed bindings close over. Collected as
    // an IReadOnlySet so the pure matcher can diff it against the battery.
    static IReadOnlySet<string> BoundScenarioNames(Assembly providerAssembly, HashSet<Type> batteryDefinitions)
    {
        var bound = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in SafeGetTypes(providerAssembly).Where(type => type is { IsClass: true, IsAbstract: false }))
        {
            var baseType = type.BaseType;
            if (baseType is { IsGenericType: true }
                && batteryDefinitions.Contains(baseType.GetGenericTypeDefinition()))
            {
                bound.Add(CleanName(baseType.GetGenericTypeDefinition()));
            }
        }
        return bound;
    }

    static string CleanName(Type genericDefinition) =>
        genericDefinition.Name.Split('`')[0];

    static Type[] SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).ToArray()!;
        }
    }
}
