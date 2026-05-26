using Edict.Tests.Conformance.Idempotency;

namespace Edict.Azure.Tests.Idempotency;

[Collection(AzureClusterCollection.Name)]
public sealed class UnhandledEventTypeRingSlotTests
    : UnhandledEventTypeRingSlotScenarios<AzureClusterFixture>
{
    public UnhandledEventTypeRingSlotTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
