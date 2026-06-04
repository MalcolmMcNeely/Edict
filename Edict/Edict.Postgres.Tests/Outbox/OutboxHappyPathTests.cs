using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Outbox;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class OutboxHappyPathTests(PostgresPersistenceFixture fixture)
    : OutboxHappyPathScenarios<PostgresPersistenceFixture>(fixture);
