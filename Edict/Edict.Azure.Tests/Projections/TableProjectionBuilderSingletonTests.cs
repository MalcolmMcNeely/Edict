using Edict.Tests.Conformance.Projections;

namespace Edict.Azure.Tests.Projections;

/// <summary>
/// Azurite binding for <see cref="TableProjectionSingletonScenarios{TFixture}"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class TableProjectionBuilderSingletonTests
    : TableProjectionSingletonScenarios<AzureClusterFixture>
{
    public TableProjectionBuilderSingletonTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
