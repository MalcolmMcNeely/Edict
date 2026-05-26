using Edict.Tests.Conformance.DeadLetter;

namespace Edict.Azure.Tests.DeadLetter;

[Collection(AzureClusterCollection.Name)]
public sealed class TableBackedDeadLetterRepositoryAzureTests(AzureClusterFixture fixture)
    : TableBackedDeadLetterRepositoryScenarios<AzureClusterFixture>(fixture);
