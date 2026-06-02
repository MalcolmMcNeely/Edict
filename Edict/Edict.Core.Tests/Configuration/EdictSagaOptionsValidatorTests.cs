using Edict.Contracts.Configuration;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Configuration;

public sealed class EdictSagaOptionsValidatorTests
{
    [Fact]
    public void DefaultTimeout_ShouldShipFiniteAtSevenDays()
    {
        Assert.Equal(TimeSpan.FromDays(7), new EdictSagaOptions().DefaultTimeout);
    }

    [Fact]
    public Task Validate_ShouldReturnNoFailures_WhenDefaultIsSevenDays()
    {
        var failures = EdictSagaOptionsValidator.Validate(new EdictSagaOptions());

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReturnNoFailures_WhenDefaultTimeoutIsNull()
    {
        var failures = EdictSagaOptionsValidator.Validate(new EdictSagaOptions
        {
            DefaultTimeout = null,
        });

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReportFailure_WhenDefaultTimeoutIsZero()
    {
        var failures = EdictSagaOptionsValidator.Validate(new EdictSagaOptions
        {
            DefaultTimeout = TimeSpan.Zero,
        });

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReportFailure_WhenDefaultTimeoutIsNegative()
    {
        var failures = EdictSagaOptionsValidator.Validate(new EdictSagaOptions
        {
            DefaultTimeout = TimeSpan.FromMinutes(-5),
        });

        return Verify(failures);
    }
}
