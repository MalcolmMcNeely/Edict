using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Outbox;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class OutboxStateAtomicityTests(PostgresPersistenceFixture fixture)
    : OutboxStateAtomicityScenarios<PostgresPersistenceFixture>(fixture);
