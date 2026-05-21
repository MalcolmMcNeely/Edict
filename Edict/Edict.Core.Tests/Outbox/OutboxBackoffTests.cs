using Edict.Contracts.Configuration;
using Edict.Core.Outbox;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Outbox;

public sealed class OutboxBackoffTests
{
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
    static readonly Guid EntryA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid EntryB = new("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public Task NextAttemptUtc_ShouldGrowExponentiallyThenClampToCeiling()
    {
        var options = new EdictOptions();

        var schedule = Enumerable.Range(0, 12)
            .Select(attemptCount => new
            {
                attemptCount,
                NextAttemptUtc = OutboxBackoff.NextAttemptUtc(attemptCount, Now, EntryA, options),
            })
            .ToArray();

        return Verify(schedule).DontScrubDateTimes();
    }

    [Fact]
    public Task NextAttemptUtc_ShouldSpreadEntriesByDeterministicJitter_PreventingStampede()
    {
        var options = new EdictOptions();

        // Same attempt count, two different entries: identical exponential
        // term, different jitter offset — proves the anti-stampede spread.
        var spread = new
        {
            EntryA_attempt5 = OutboxBackoff.NextAttemptUtc(5, Now, EntryA, options),
            EntryB_attempt5 = OutboxBackoff.NextAttemptUtc(5, Now, EntryB, options),
            // Reproducible: the same entry+attempt always yields the same instant.
            EntryA_attempt5_again = OutboxBackoff.NextAttemptUtc(5, Now, EntryA, options),
        };

        return Verify(spread).DontScrubDateTimes();
    }

    [Fact]
    public Task NextAttemptUtc_ShouldHonourConsumerConfiguredBaseAndCeiling()
    {
        var options = new EdictOptions
        {
            OutboxBaseDelay = TimeSpan.FromSeconds(10),
            OutboxMaxDelay = TimeSpan.FromMinutes(2),
            OutboxJitterFraction = 0,
        };

        var schedule = Enumerable.Range(0, 10)
            .Select(attemptCount => new
            {
                attemptCount,
                NextAttemptUtc = OutboxBackoff.NextAttemptUtc(attemptCount, Now, EntryA, options),
            })
            .ToArray();

        return Verify(schedule).DontScrubDateTimes();
    }
}
