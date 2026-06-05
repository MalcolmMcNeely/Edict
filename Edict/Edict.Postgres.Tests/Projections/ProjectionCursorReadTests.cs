using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Projections;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class ProjectionCursorReadTests(PostgresPersistenceFixture fixture)
    : ProjectionCursorReadScenarios<PostgresPersistenceFixture>(fixture);
