using Edict.Tests.Conformance.Outbox;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// Azurite binding for
/// <see cref="OutboxDrainOnActivationScenarios{TFixture}"/>.
/// </summary>
[Collection(AzureOutboxControllableExecutorCollection.Name)]
public sealed class OutboxDrainOnActivationTests
    : OutboxDrainOnActivationScenarios<AzureOutboxRecoveryClusterFixture>
{
    public OutboxDrainOnActivationTests(AzureOutboxRecoveryClusterFixture fixture) : base(fixture)
    {
    }
}
