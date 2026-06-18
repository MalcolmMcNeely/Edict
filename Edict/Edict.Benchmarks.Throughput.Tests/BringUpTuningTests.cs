using Edict.Substrate;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class BringUpTuningTests
{
    [Fact]
    public void FromEnvironment_AbsentVariables_YieldLoweredDefaults()
    {
        var tuning = BringUpTuning.FromEnvironment(_ => null);

        Assert.Equal(new BringUpTuning(), tuning);
    }

    [Fact]
    public void FromEnvironment_SetVariables_OverrideEveryDefault()
    {
        var values = new Dictionary<string, string?>
        {
            [BringUpTuning.TestcontainersWaitSecondsVariable] = "45",
            [BringUpTuning.HostProbeDeadlineSecondsVariable] = "15",
            [BringUpTuning.HostProbePollMillisecondsVariable] = "500",
            [BringUpTuning.WithinBootStaggerSecondsVariable] = "3",
            [BringUpTuning.CrossBootSettleSecondsVariable] = "7",
            [BringUpTuning.RetryCountVariable] = "5",
            [BringUpTuning.SetupDeadlineSecondsVariable] = "600",
        };

        var tuning = BringUpTuning.FromEnvironment(name => values[name]);

        var expected = new BringUpTuning
        {
            TestcontainersWaitTimeout = TimeSpan.FromSeconds(45),
            HostReadinessProbeDeadline = TimeSpan.FromSeconds(15),
            HostReadinessProbePollInterval = TimeSpan.FromMilliseconds(500),
            WithinBootStaggerGap = TimeSpan.FromSeconds(3),
            CrossBootSettleGap = TimeSpan.FromSeconds(7),
            RetryCount = 5,
            SetupDeadline = TimeSpan.FromSeconds(600),
        };
        Assert.Equal(expected, tuning);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("")]
    public void FromEnvironment_MalformedOrNonPositiveValues_FallBackToDefaults(string raw)
    {
        var tuning = BringUpTuning.FromEnvironment(_ => raw);

        Assert.Equal(new BringUpTuning(), tuning);
    }
}
