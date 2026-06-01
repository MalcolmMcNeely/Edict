using Edict.Tests.Conformance.Outbox;

namespace Edict.Postgres.Tests.Outbox;

[Collection(PostgresStateWriteFaultCollection.Name)]
public sealed class StateWriteFaultPostgresTests(PostgresStateWriteFaultFixture fixture)
    : StateWriteFaultScenarios<PostgresStateWriteFaultFixture>(fixture);
