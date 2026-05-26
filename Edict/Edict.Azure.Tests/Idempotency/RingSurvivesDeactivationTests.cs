using Edict.Tests.Conformance.Idempotency;

namespace Edict.Azure.Tests.Idempotency;

[Collection(AzureClusterCollection.Name)]
public sealed class RingSurvivesDeactivationTests
    : RingSurvivesDeactivationScenarios<AzureClusterFixture>
{
    public RingSurvivesDeactivationTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
