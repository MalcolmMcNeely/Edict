using Edict.Postgres.Tests.Outbox;
using Edict.Tests.Conformance.DeadLetter;

namespace Edict.Postgres.Tests.DeadLetter;

[Collection(PostgresOutboxControllableExecutorCollection.Name)]
public sealed class UnregisteredTypePromotesToDeadLetterPostgresTests(PostgresOutboxControllableExecutorFixture fixture)
    : UnregisteredTypePromotesToDeadLetterScenarios<PostgresOutboxControllableExecutorFixture>(fixture);
