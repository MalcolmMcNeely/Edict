using Edict.Tests.Conformance.Projections;

namespace Edict.Azure.Tests.Projections;

/// <summary>
/// Azurite binding for <see cref="TableProjectionConsumerRowKeyScenarios{TFixture}"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class TableProjectionBuilderConsumerRowKeyTests
    : TableProjectionConsumerRowKeyScenarios<AzureClusterFixture>
{
    public TableProjectionBuilderConsumerRowKeyTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
