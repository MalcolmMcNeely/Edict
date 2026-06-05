using Edict.Contracts.Configuration;
using Edict.Contracts.Schedules;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Configuration;

public sealed class EdictCommandHandlerScheduleOptionsValidatorTests
{
    [Fact]
    public void DefaultTimeout_ShouldShipFiniteAtSevenDays()
    {
        Assert.Equal(TimeSpan.FromDays(7), new EdictCommandHandlerScheduleOptions().DefaultTimeout);
    }

    [Fact]
    public Task Validate_ShouldReturnNoFailures_WhenDefaultIsSevenDays()
    {
        var failures = EdictCommandHandlerScheduleOptionsValidator.Validate(new EdictCommandHandlerScheduleOptions());

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReturnNoFailures_WhenDefaultTimeoutIsNull()
    {
        var failures = EdictCommandHandlerScheduleOptionsValidator.Validate(new EdictCommandHandlerScheduleOptions
        {
            DefaultTimeout = null,
        });

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReturnNoFailures_WhenDefaultTimeoutIsUnbounded()
    {
        var failures = EdictCommandHandlerScheduleOptionsValidator.Validate(new EdictCommandHandlerScheduleOptions
        {
            DefaultTimeout = EdictSchedule.Unbounded,
        });

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReportFailure_WhenDefaultTimeoutIsZero()
    {
        var failures = EdictCommandHandlerScheduleOptionsValidator.Validate(new EdictCommandHandlerScheduleOptions
        {
            DefaultTimeout = TimeSpan.Zero,
        });

        return Verify(failures);
    }

    [Fact]
    public Task Validate_ShouldReportFailure_WhenDefaultTimeoutIsNegative()
    {
        var failures = EdictCommandHandlerScheduleOptionsValidator.Validate(new EdictCommandHandlerScheduleOptions
        {
            DefaultTimeout = TimeSpan.FromMinutes(-5),
        });

        return Verify(failures);
    }
}
