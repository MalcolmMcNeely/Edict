using Edict.Contracts.Configuration;
using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.Tenancy;

using Xunit;

namespace Edict.Postgres.Tests.Tenancy;

/// <summary>
/// A Postgres persistence-axis fixture with tenancy on and the real publish
/// executor swapped for <c>ControllableOutboxExecutor</c> at
/// <c>OutboxMaxAttempts</c> = 2, so a poisoned outbox entry on a tenant-scoped
/// grain promotes to a dead-letter row after two failed attempts. Backs the
/// tenant-tagged dead-letter scenario on a real persistence backend.
/// </summary>
public sealed class PostgresTenancyDeadLetterFixture : PostgresPersistenceFixtureBase
{
    protected override bool EnableTenancy => true;

    protected override bool ReplacePublishExecutorWithControllable => true;

    protected override Action<EdictOptions>? ConfigureOptions => options =>
    {
        options.OutboxMaxAttempts = 2;
        options.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
        options.OutboxJitterFraction = 0;
    };

    // Tenancy fails closed at an origin send with no ambient tenant, and the base
    // bring-up issues a warm-up command, so seed a tenant before the cluster deploys.
    public override async Task InitializeAsync()
    {
        ConformanceTenantSource.Current = EdictTenantId.Of("warmup");
        await base.InitializeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTenancyDeadLetterCollection
    : ICollectionFixture<PostgresTenancyDeadLetterFixture>
{
    public const string Name = "PostgresTenancyDeadLetter";
}
