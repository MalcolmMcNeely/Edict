using Edict.Core.Outbox;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Outbox;

// Backoff is a pure function of AttemptCount (ADR 0019): exponential from a
// base delay, clamped to a ceiling so a long outage cannot push NextAttemptUtc
// arbitrarily far out. Fixed inputs; the literal timestamps are the assertion.

public sealed class OutboxBackoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task NextAttemptUtc_ShouldGrowExponentiallyThenClampToCeiling()
    {
        var baseDelay = TimeSpan.FromSeconds(2);

        var schedule = Enumerable.Range(0, 12)
            .Select(attemptCount => new
            {
                attemptCount,
                NextAttemptUtc = OutboxBackoff.NextAttemptUtc(attemptCount, Now, baseDelay),
            })
            .ToArray();

        return Verify(schedule).DontScrubDateTimes();
    }
}
