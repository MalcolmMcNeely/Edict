using Edict.Tests.Conformance.Outbox;

namespace Edict.Azure.Tests.Outbox;

[Collection(AzureStateWriteFaultCollection.Name)]
public sealed class StateWriteFaultAzureTests(AzureStateWriteFaultClusterFixture fixture)
    : StateWriteFaultScenarios<AzureStateWriteFaultClusterFixture>(fixture);
