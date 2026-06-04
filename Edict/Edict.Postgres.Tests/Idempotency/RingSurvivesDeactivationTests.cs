using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Idempotency;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class RingSurvivesDeactivationTests(PostgresPersistenceFixture fixture)
    : RingSurvivesDeactivationScenarios<PostgresPersistenceFixture>(fixture);
