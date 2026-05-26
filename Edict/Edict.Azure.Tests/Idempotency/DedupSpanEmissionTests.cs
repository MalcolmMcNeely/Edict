using Edict.Tests.Conformance.Idempotency;

namespace Edict.Azure.Tests.Idempotency;

[Collection(AzureClusterCollection.Name)]
public sealed class DedupSpanEmissionTests
    : DedupSpanEmissionScenarios<AzureClusterFixture>
{
    public DedupSpanEmissionTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
