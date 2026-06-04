using Xunit;

namespace Edict.Postgres.Persistence.Tests;

/// <summary>
/// The default persistence-axis fixture: real Postgres grain storage + table
/// store behind a dumb <c>MemoryStreams</c> reference, with the shipped outbox
/// configuration and the real publish executor. Backs every persistence
/// scenario that does not need a controllable fault injected — atomicity, the
/// happy path, table projections, ring survival, saga-timeout terminal
/// dead-lettering, and the table-backed dead-letter read seam.
/// </summary>
public sealed class PostgresPersistenceFixture : PostgresPersistenceFixtureBase
{
}

[CollectionDefinition(Name)]
public sealed class PostgresPersistenceCollection : ICollectionFixture<PostgresPersistenceFixture>
{
    public const string Name = "PostgresPersistence";
}
