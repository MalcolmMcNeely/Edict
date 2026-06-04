using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Persistence.Tests.Outbox;

[Collection(PostgresPersistenceControllableExecutorCollection.Name)]
public sealed class OutboxRecoveryAfterCrashTests(PostgresPersistenceControllableExecutorFixture fixture)
    : OutboxRecoveryAfterCrashScenarios<PostgresPersistenceControllableExecutorFixture>(fixture);
