using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Persistence.Tests.Idempotency;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class RingSurvivesDeactivationTests(PostgresPersistenceFixture fixture)
    : RingSurvivesDeactivationScenarios<PostgresPersistenceFixture>(fixture);
