using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// The default persistence-axis fixture: real Azure Blob grain storage + Azure
/// Table store behind a dumb <c>MemoryStreams</c> reference, with the shipped
/// outbox configuration and the real publish executor. Backs every persistence
/// scenario that does not need a controllable fault injected — atomicity, the
/// happy path, table projections, ring survival, saga-timeout terminal
/// dead-lettering, and the table-backed dead-letter read seam.
/// </summary>
public sealed class AzurePersistenceFixture : AzurePersistenceFixtureBase
{
}

[CollectionDefinition(Name)]
public sealed class AzurePersistenceCollection : ICollectionFixture<AzurePersistenceFixture>
{
    public const string Name = "AzurePersistence";
}
