using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Azure.Tests.ClaimCheck;

[Collection(AzureClaimCheckCollection.Name)]
public sealed class ReceiverUnwrapsClaimCheckTests(AzureClaimCheckClusterFixture fixture)
    : ReceiverUnwrapsClaimCheckScenarios<AzureClaimCheckClusterFixture>(fixture);
