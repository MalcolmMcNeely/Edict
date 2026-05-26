using Edict.Tests.Conformance.Outbox;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// Azurite binding for <see cref="OutboxStateAtomicityScenarios{TFixture}"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class OutboxStateAtomicityTests : OutboxStateAtomicityScenarios<AzureClusterFixture>
{
    public OutboxStateAtomicityTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
