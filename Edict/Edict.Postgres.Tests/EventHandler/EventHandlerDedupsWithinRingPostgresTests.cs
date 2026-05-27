using Edict.Tests.Conformance.EventHandler;

namespace Edict.Postgres.Tests.EventHandler;

[Collection(PostgresClusterCollection.Name)]
public sealed class EventHandlerDedupsWithinRingPostgresTests(PostgresClusterFixture fixture)
    : EventHandlerDedupsWithinRingScenarios<PostgresClusterFixture>(fixture);
