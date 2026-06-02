using Edict.Tests.Conformance.Sagas;

namespace Edict.Azure.Tests.Sagas;

[Collection(AzureClusterCollection.Name)]
public sealed class SagaTimeoutTerminalDeadLetterTests
    : SagaTimeoutTerminalDeadLetterScenarios<AzureClusterFixture>
{
    public SagaTimeoutTerminalDeadLetterTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
