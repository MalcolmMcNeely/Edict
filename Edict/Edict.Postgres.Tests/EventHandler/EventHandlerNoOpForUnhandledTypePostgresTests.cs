using Edict.Tests.Conformance.EventHandler;

namespace Edict.Postgres.Tests.EventHandler;

[Collection(PostgresClusterCollection.Name)]
public sealed class EventHandlerNoOpForUnhandledTypePostgresTests(PostgresClusterFixture fixture)
    : EventHandlerNoOpForUnhandledTypeScenarios<PostgresClusterFixture>(fixture);
