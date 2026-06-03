using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Azure.Tests.ClaimCheck;

[Collection(AzureClaimCheckCollection.Name)]
public sealed class AzureClaimCheckKeyContractTests(AzureClaimCheckClusterFixture fixture)
    : ClaimCheckKeyContractScenarios<AzureClaimCheckClusterFixture>(fixture);
