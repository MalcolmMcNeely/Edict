using Edict.Tests.Conformance.Sagas;

namespace Edict.Azure.Tests.Sagas;

[Collection(AzureClusterCollection.Name)]
public sealed class SagaSendCommandEffectDeliversTests
    : SagaSendCommandEffectDeliversScenarios<AzureClusterFixture>
{
    public SagaSendCommandEffectDeliversTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
