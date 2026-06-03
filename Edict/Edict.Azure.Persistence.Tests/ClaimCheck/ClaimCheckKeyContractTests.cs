using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Azure.Persistence.Tests.ClaimCheck;

[Collection(AzurePersistenceClaimCheckCollection.Name)]
public sealed class ClaimCheckKeyContractTests(AzurePersistenceClaimCheckFixture fixture)
    : ClaimCheckKeyContractScenarios<AzurePersistenceClaimCheckFixture>(fixture);
