using Edict.Contracts.Configuration;

using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Persistence-axis fixture whose silo arms the <c>DeadLetterPromoter</c>
/// degrade-arm failing executors and a superset route resolver, at
/// <c>OutboxMaxAttempts</c> = 2 with zero backoff. A staged poisoned outbox entry
/// exhausts its attempts on the next reminder tick and promotes through a degrade
/// arm; zero backoff makes the entry immediately re-ready so the convergence
/// drain needs no clock gate. Backs the promoter degrade-arm convergence scenario.
/// </summary>
public sealed class AzurePersistenceDeadLetterDegradeFixture : AzurePersistenceFixtureBase
{
    protected override bool WireDeadLetterDegradeArms => true;

    protected override Action<EdictOptions>? ConfigureOptions => options =>
    {
        options.OutboxMaxAttempts = 2;
        // The smallest backoff the wiring validator accepts; a probe tick's grain
        // round-trip already outlasts it, so attempts accumulate across ticks with
        // no wall-clock gate in the scenario.
        options.OutboxBaseDelay = TimeSpan.FromMilliseconds(1);
        options.OutboxJitterFraction = 0;
    };
}

[CollectionDefinition(Name)]
public sealed class AzurePersistenceDeadLetterDegradeCollection
    : ICollectionFixture<AzurePersistenceDeadLetterDegradeFixture>
{
    public const string Name = "AzurePersistenceDeadLetterDegrade";
}
