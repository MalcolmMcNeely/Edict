using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Postgres.Tests.ClaimCheck;

[Collection(PostgresMissingClaimCheckCollection.Name)]
public sealed class MissingClaimCheckDeadLetterClassificationPostgresTests(PostgresMissingClaimCheckClusterFixture fixture)
    : MissingClaimCheckDeadLetterClassificationScenarios<PostgresMissingClaimCheckClusterFixture>(fixture);
