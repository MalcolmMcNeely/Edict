using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Azure.Tests.ClaimCheck;

[Collection(AzureClaimCheckCollection.Name)]
public sealed class LargePayloadPublishesViaBlobTests(AzureClaimCheckClusterFixture fixture)
    : LargePayloadPublishesViaBlobScenarios<AzureClaimCheckClusterFixture>(fixture);
