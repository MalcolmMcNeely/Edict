using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Sagas;

[Collection(PostgresPersistenceSagaTimeoutCollection.Name)]
public sealed class SagaTimeoutCompensationFailureTests(PostgresPersistenceSagaTimeoutFixture fixture)
    : SagaTimeoutCompensationFailureScenarios<PostgresPersistenceSagaTimeoutFixture>(fixture);
