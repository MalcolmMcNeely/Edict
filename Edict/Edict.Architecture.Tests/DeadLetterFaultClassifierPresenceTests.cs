using System.Reflection;

using Edict.Azure.Persistence;
using Edict.Azure.Streaming;
using Edict.Kafka;
using Edict.Postgres;

using Xunit;

namespace Edict.Architecture.Tests;

// Drift guard + registry: each shipped provider assembly must contribute at
// least one dead-letter fault classifier, so a regression that silently drops a
// substrate back to the catch-all Unhandled bucket fails here at test time
// rather than in production telemetry. The enumerated list is the registry of
// "substrates that must classify" — adding a fifth substrate adds a row here.
// Matched by interface full name because IDeadLetterFaultClassifier is internal
// to Edict.Core, so the guard needs no InternalsVisibleTo of its own.
public class DeadLetterFaultClassifierPresenceTests
{
    const string ClassifierInterfaceFullName = "Edict.Core.DeadLetter.IDeadLetterFaultClassifier";

    public static IEnumerable<object[]> ProviderAssemblies() =>
    [
        [typeof(EdictPostgresSiloBuilderExtensions).Assembly],
        [typeof(EdictKafkaSiloBuilderExtensions).Assembly],
        [typeof(EdictAzureStreamingSiloBuilderExtensions).Assembly],
        [typeof(EdictAzurePersistenceSiloBuilderExtensions).Assembly],
    ];

    [Theory]
    [MemberData(nameof(ProviderAssemblies))]
    public void EveryProviderAssembly_ShipsAtLeastOneFaultClassifier(Assembly providerAssembly)
    {
        var classifiers = providerAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(contract => contract.FullName == ClassifierInterfaceFullName))
            .ToList();

        Assert.True(
            classifiers.Count > 0,
            $"{providerAssembly.GetName().Name} ships no IDeadLetterFaultClassifier — its driver faults would silently bucket as Unhandled.");
    }
}
