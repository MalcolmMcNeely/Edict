using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.ClaimCheck;

[Collection(PostgresPersistenceClaimCheckThresholdCollection.Name)]
public sealed class ClaimCheckThresholdBoundaryTests(PostgresPersistenceClaimCheckThresholdFixture fixture)
    : ClaimCheckThresholdBoundaryScenarios<PostgresPersistenceClaimCheckThresholdFixture>(fixture);
