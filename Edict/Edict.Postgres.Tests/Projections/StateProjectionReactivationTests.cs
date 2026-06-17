using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Projections;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class StateProjectionReactivationTests(PostgresPersistenceFixture fixture)
    : StateProjectionReactivationScenarios<PostgresPersistenceFixture>(fixture);
