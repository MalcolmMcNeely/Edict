using Edict.Tests.Conformance.Projections;

namespace Edict.Azure.Tests.Projections;

/// <summary>
/// Azurite binding for <see cref="ProjectionDeliveryScenarios{TFixture}"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class ProjectionBuilderTests : ProjectionDeliveryScenarios<AzureClusterFixture>
{
    public ProjectionBuilderTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
