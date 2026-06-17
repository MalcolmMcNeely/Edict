using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.ClaimCheck;

[Collection(AzurePersistenceClaimCheckTransientRecoveryCollection.Name)]
public sealed class ClaimCheckTransientRecoveryTests(AzurePersistenceClaimCheckTransientRecoveryFixture fixture)
    : ClaimCheckTransientRecoveryScenarios<AzurePersistenceClaimCheckTransientRecoveryFixture>(fixture);
