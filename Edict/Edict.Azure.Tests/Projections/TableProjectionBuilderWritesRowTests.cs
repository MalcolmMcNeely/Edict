using Edict.Tests.Conformance.Projections;

namespace Edict.Azure.Tests.Projections;

/// <summary>
/// Azurite binding for <see cref="TableProjectionWritesRowScenarios{TFixture}"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class TableProjectionBuilderWritesRowTests
    : TableProjectionWritesRowScenarios<AzureClusterFixture>
{
    public TableProjectionBuilderWritesRowTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
