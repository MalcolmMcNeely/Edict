using Edict.Tests.Conformance.Projections;

namespace Edict.Azure.Tests.Projections;

/// <summary>
/// Azurite binding for <see cref="TableProjectionIncrementsOnSubsequentEventScenarios{TFixture}"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class TableProjectionBuilderIncrementsOnSubsequentEventTests
    : TableProjectionIncrementsOnSubsequentEventScenarios<AzureClusterFixture>
{
    public TableProjectionBuilderIncrementsOnSubsequentEventTests(AzureClusterFixture fixture)
        : base(fixture)
    {
    }
}
