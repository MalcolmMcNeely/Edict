using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.ClaimCheck;

[Collection(PostgresPersistenceClaimCheckCollection.Name)]
public sealed class TableProjectionReceivesClaimCheckTests(PostgresPersistenceClaimCheckFixture fixture)
    : TableProjectionReceivesClaimCheckScenarios<PostgresPersistenceClaimCheckFixture>(fixture);
