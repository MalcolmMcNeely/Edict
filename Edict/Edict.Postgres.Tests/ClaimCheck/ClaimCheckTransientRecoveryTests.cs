using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.ClaimCheck;

[Collection(PostgresPersistenceClaimCheckTransientRecoveryCollection.Name)]
public sealed class ClaimCheckTransientRecoveryTests(PostgresPersistenceClaimCheckTransientRecoveryFixture fixture)
    : ClaimCheckTransientRecoveryScenarios<PostgresPersistenceClaimCheckTransientRecoveryFixture>(fixture);
