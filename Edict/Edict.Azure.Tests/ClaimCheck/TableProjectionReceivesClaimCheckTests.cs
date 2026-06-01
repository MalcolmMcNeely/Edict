using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Azure.Tests.ClaimCheck;

[Collection(AzureClaimCheckCollection.Name)]
public sealed class TableProjectionReceivesClaimCheckTests(AzureClaimCheckClusterFixture fixture)
    : TableProjectionReceivesClaimCheckScenarios<AzureClaimCheckClusterFixture>(fixture);
