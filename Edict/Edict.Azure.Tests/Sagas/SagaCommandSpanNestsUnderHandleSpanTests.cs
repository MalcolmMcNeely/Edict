using Edict.Tests.Conformance.Sagas;

namespace Edict.Azure.Tests.Sagas;

[Collection(AzureClusterCollection.Name)]
public sealed class SagaCommandSpanNestsUnderHandleSpanTests
    : SagaCommandSpanNestsUnderHandleSpanScenarios<AzureClusterFixture>
{
    public SagaCommandSpanNestsUnderHandleSpanTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
