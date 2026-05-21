using Edict.Contracts.Configuration;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Configuration;

// Pure-function tests over EdictOptions. The validator returns the
// full failure list per invocation so a host with two problems sees two
// problems; one Verify snapshot per scenario keeps the messages themselves as
// the assertion (drift in wording fails CI).
public sealed class EdictOptionsValidatorTests
{
    [Fact]
    public Task Validate_ShouldReturnNoFailures_WhenAllValuesAreDefault()
    {
        var failures = EdictOptionsValidator.Validate(new EdictOptions());

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReportFailure_WhenOutboxJitterFractionAboveOne()
    {
        var failures = EdictOptionsValidator.Validate(new EdictOptions
        {
            OutboxJitterFraction = 1.5,
        });

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReportFailure_WhenOutboxJitterFractionBelowZero()
    {
        var failures = EdictOptionsValidator.Validate(new EdictOptions
        {
            OutboxJitterFraction = -0.1,
        });

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReportFailure_WhenOutboxMaxAttemptsBelowOne()
    {
        var failures = EdictOptionsValidator.Validate(new EdictOptions
        {
            OutboxMaxAttempts = 0,
        });

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReportFailure_WhenOutboxBaseDelayIsZero()
    {
        var failures = EdictOptionsValidator.Validate(new EdictOptions
        {
            OutboxBaseDelay = TimeSpan.Zero,
        });

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReportFailure_WhenOutboxBaseDelayExceedsMaxDelay()
    {
        var failures = EdictOptionsValidator.Validate(new EdictOptions
        {
            OutboxBaseDelay = TimeSpan.FromMinutes(10),
            OutboxMaxDelay = TimeSpan.FromMinutes(5),
        });

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReportFailure_WhenOutboxDrainReminderPeriodBelowOneMinute()
    {
        var failures = EdictOptionsValidator.Validate(new EdictOptions
        {
            OutboxDrainReminderPeriod = TimeSpan.FromSeconds(30),
        });

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReportFailure_WhenIdempotencyWindowSizeBelowOne()
    {
        var failures = EdictOptionsValidator.Validate(new EdictOptions
        {
            IdempotencyWindowSize = 0,
        });

        return Verify(failures);
    }

    // The validator's job is to report the full list per invocation so a host
    // with three problems sees three problems — proof the failure-accumulation
    // contract holds across rules.
    [Fact]
    public Task Validate_ShouldReportEveryFailure_WhenMultipleValuesAreInvalid()
    {
        var failures = EdictOptionsValidator.Validate(new EdictOptions
        {
            OutboxJitterFraction = 2.0,
            OutboxMaxAttempts = 0,
            OutboxDrainReminderPeriod = TimeSpan.FromSeconds(10),
        });

        return Verify(failures);
    }
}
