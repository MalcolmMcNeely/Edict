using Edict.Tests.Conformance.Idempotency;

namespace Edict.Postgres.Tests.Idempotency;

[Collection(PostgresClusterCollection.Name)]
public sealed class UnhandledEventTypeRingSlotPostgresTests(PostgresClusterFixture fixture)
    : UnhandledEventTypeRingSlotScenarios<PostgresClusterFixture>(fixture);
