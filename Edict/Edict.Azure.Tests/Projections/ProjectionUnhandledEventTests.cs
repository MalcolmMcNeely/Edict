using Edict.Tests.Conformance.Projections;

namespace Edict.Azure.Tests.Projections;

/// <summary>
/// Azurite binding for <see cref="ProjectionUnhandledEventScenarios{TFixture}"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class ProjectionUnhandledEventTests : ProjectionUnhandledEventScenarios<AzureClusterFixture>
{
    public ProjectionUnhandledEventTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
