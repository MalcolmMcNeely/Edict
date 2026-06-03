using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Postgres.Tests.ClaimCheck;

[Collection(PostgresClaimCheckCollection.Name)]
public sealed class PostgresClaimCheckKeyContractTests(PostgresClaimCheckClusterFixture fixture)
    : ClaimCheckKeyContractScenarios<PostgresClaimCheckClusterFixture>(fixture);
