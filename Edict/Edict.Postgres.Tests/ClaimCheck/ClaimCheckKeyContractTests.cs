using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Postgres.Tests.ClaimCheck;

[Collection(PostgresPersistenceClaimCheckCollection.Name)]
public sealed class ClaimCheckKeyContractTests(PostgresPersistenceClaimCheckFixture fixture)
    : ClaimCheckKeyContractScenarios<PostgresPersistenceClaimCheckFixture>(fixture);
