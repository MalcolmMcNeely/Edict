using Edict.Contracts.Configuration;

using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Persistence-axis fixture for the missing-claim-check dead-letter
/// classification scenario: the real Azure Blob claim-check store is wired, and
/// the outbox is tuned to dead-letter quickly so a pointer envelope whose body
/// the store never held promotes to a <c>Substrate</c>-classified dead-letter row
/// within the test window.
/// </summary>
public sealed class AzurePersistenceBlobMissingFixture : AzurePersistenceFixtureBase
{
    protected override Action<EdictOptions>? ConfigureOptions => options =>
    {
        options.OutboxMaxAttempts = 3;
        options.OutboxBaseDelay = TimeSpan.FromMilliseconds(30);
        options.OutboxMaxDelay = TimeSpan.FromMilliseconds(60);
        options.OutboxJitterFraction = 0;
    };
}

[CollectionDefinition(Name)]
public sealed class AzurePersistenceBlobMissingCollection
    : ICollectionFixture<AzurePersistenceBlobMissingFixture>
{
    public const string Name = "AzurePersistenceBlobMissing";
}
