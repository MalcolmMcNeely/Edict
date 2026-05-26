using Edict.Tests.Conformance.EventHandler;

namespace Edict.Azure.Tests.EventHandler;

/// <summary>
/// Azurite/Testcontainers binding for
/// <see cref="EventHandlerDedupsWithinRingScenarios{TFixture}"/>. Inherits the
/// scenario from <c>Edict.Tests.Conformance</c>; the [Fact] runs unmodified
/// against the shared <see cref="AzureClusterFixture"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class EventHandlerDedupsWithinRingTests
    : EventHandlerDedupsWithinRingScenarios<AzureClusterFixture>
{
    public EventHandlerDedupsWithinRingTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
