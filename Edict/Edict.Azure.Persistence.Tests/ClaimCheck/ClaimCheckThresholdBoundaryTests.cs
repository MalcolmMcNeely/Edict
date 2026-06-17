using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.ClaimCheck;

[Collection(AzurePersistenceClaimCheckThresholdCollection.Name)]
public sealed class ClaimCheckThresholdBoundaryTests(AzurePersistenceClaimCheckThresholdFixture fixture)
    : ClaimCheckThresholdBoundaryScenarios<AzurePersistenceClaimCheckThresholdFixture>(fixture);
