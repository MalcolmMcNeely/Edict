using Edict.Tests.Conformance.Outbox;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// Azurite binding for
/// <see cref="OutboxRecoveryAfterCrashScenarios{TFixture}"/>.
/// </summary>
[Collection(AzureOutboxControllableExecutorCollection.Name)]
public sealed class OutboxRecoveryAfterCrashTests
    : OutboxRecoveryAfterCrashScenarios<AzureOutboxRecoveryClusterFixture>
{
    public OutboxRecoveryAfterCrashTests(AzureOutboxRecoveryClusterFixture fixture) : base(fixture)
    {
    }
}
