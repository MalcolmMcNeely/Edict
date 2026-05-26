using Edict.Tests.Conformance.EventHandler;

namespace Edict.Azure.Tests.EventHandler;

/// <summary>
/// Azurite/Testcontainers binding for
/// <see cref="EventHandlerNoOpForUnhandledTypeScenarios{TFixture}"/>. Inherits
/// the scenario from <c>Edict.Tests.Conformance</c>; the [Fact] runs
/// unmodified against the shared <see cref="AzureClusterFixture"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class EventHandlerNoOpForUnhandledTypeTests
    : EventHandlerNoOpForUnhandledTypeScenarios<AzureClusterFixture>
{
    public EventHandlerNoOpForUnhandledTypeTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
