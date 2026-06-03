using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.ClaimCheck;

[Collection(AzurePersistenceClaimCheckCollection.Name)]
public sealed class TableProjectionReceivesClaimCheckTests(AzurePersistenceClaimCheckFixture fixture)
    : TableProjectionReceivesClaimCheckScenarios<AzurePersistenceClaimCheckFixture>(fixture);
