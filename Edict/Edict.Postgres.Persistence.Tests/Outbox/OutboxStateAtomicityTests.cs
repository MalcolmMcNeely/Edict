using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Persistence.Tests.Outbox;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class OutboxStateAtomicityTests(PostgresPersistenceFixture fixture)
    : OutboxStateAtomicityScenarios<PostgresPersistenceFixture>(fixture);
