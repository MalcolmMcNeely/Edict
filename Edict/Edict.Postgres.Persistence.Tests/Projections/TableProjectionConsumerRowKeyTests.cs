using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Persistence.Tests.Projections;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class TableProjectionConsumerRowKeyTests(PostgresPersistenceFixture fixture)
    : TableProjectionConsumerRowKeyScenarios<PostgresPersistenceFixture>(fixture);
