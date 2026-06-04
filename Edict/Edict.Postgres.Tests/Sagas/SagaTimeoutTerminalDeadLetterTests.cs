using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Sagas;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class SagaTimeoutTerminalDeadLetterTests(PostgresPersistenceFixture fixture)
    : SagaTimeoutTerminalDeadLetterScenarios<PostgresPersistenceFixture>(fixture);
