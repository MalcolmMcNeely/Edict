using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Persistence.Tests.ClaimCheck;

[Collection(PostgresPersistenceClaimCheckCollection.Name)]
public sealed class TableProjectionReceivesClaimCheckTests(PostgresPersistenceClaimCheckFixture fixture)
    : TableProjectionReceivesClaimCheckScenarios<PostgresPersistenceClaimCheckFixture>(fixture);
