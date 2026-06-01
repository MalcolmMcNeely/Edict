using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Azure.Tests.ClaimCheck;

[Collection(AzureClaimCheckCollection.Name)]
public sealed class SagaReceivesClaimCheckTests(AzureClaimCheckClusterFixture fixture)
    : SagaReceivesClaimCheckScenarios<AzureClaimCheckClusterFixture>(fixture);
