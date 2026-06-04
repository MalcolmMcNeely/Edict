using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Persistence.Tests.ClaimCheck;

[Collection(PostgresPersistenceMissingClaimCheckCollection.Name)]
public sealed class MissingClaimCheckDeadLetterClassificationTests(PostgresPersistenceMissingClaimCheckFixture fixture)
    : MissingClaimCheckDeadLetterClassificationScenarios<PostgresPersistenceMissingClaimCheckFixture>(fixture);
