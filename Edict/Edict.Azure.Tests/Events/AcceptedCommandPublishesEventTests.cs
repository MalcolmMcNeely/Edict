using Edict.Tests.Conformance.Events;

namespace Edict.Azure.Tests.Events;

/// <summary>
/// Azurite/Testcontainers binding for
/// <see cref="AcceptedCommandPublishesEventScenarios{TFixture}"/>. Inherits the
/// scenario from <c>Edict.Tests.Conformance</c>; the [Fact] runs unmodified
/// against the shared <see cref="AzureClusterFixture"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class AcceptedCommandPublishesEventTests
    : AcceptedCommandPublishesEventScenarios<AzureClusterFixture>
{
    public AcceptedCommandPublishesEventTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
