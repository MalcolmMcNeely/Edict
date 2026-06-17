using Edict.Contracts.Configuration;
using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.Tenancy;

using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// An Azure persistence-axis fixture with tenancy on and the real publish executor
/// swapped for <c>ControllableOutboxExecutor</c> at <c>OutboxMaxAttempts</c> = 2,
/// so a poisoned outbox entry on a tenant-scoped grain promotes to a dead-letter
/// row after two failed attempts. Backs the tenant-tagged dead-letter scenario: the
/// row's tenant tag is recovered from the source grain's composed key and lands in
/// the real dead-letter table.
/// </summary>
public sealed class AzureTenancyDeadLetterFixture : AzurePersistenceFixtureBase
{
    protected override bool EnableTenancy => true;

    protected override bool ReplacePublishExecutorWithControllable => true;

    protected override Action<EdictOptions>? ConfigureOptions => options =>
    {
        options.OutboxMaxAttempts = 2;
        options.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
        options.OutboxJitterFraction = 0;
    };

    // Tenancy fails closed at an origin send with no ambient tenant, so seed one
    // before the cluster deploys rather than leave the static at its null default.
    public override async Task InitializeAsync()
    {
        ConformanceTenantSource.Current = EdictTenantId.Of("warmup");
        await base.InitializeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class AzureTenancyDeadLetterCollection
    : ICollectionFixture<AzureTenancyDeadLetterFixture>
{
    public const string Name = "AzureTenancyDeadLetter";
}
