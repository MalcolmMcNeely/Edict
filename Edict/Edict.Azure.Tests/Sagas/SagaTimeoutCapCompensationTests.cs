using Edict.Tests.Conformance.Sagas;

namespace Edict.Azure.Tests.Sagas;

[Collection(AzureClusterCollection.Name)]
public sealed class SagaTimeoutCapCompensationTests
    : SagaTimeoutCapCompensationScenarios<AzureClusterFixture>
{
    public SagaTimeoutCapCompensationTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
