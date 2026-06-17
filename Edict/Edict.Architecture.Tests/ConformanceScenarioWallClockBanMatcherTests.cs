using Xunit;

namespace Edict.Architecture.Tests;

public class ConformanceScenarioWallClockBanMatcherTests
{
    [Fact]
    public void ScenarioFileContainingTaskDelay_IsFlaggedWithLineNumber()
    {
        // Arrange
        const string source = """
            await PumpUntilAsync(probe);
            await Task.Delay(TimeSpan.FromSeconds(2));
            Assert.True(probe.Saw);
            """;

        // Act
        var violations = ConformanceScenarioWallClockBan.FindWallClockSleeps("RedeliverAfterThrowScenarios.cs", source);

        // Assert
        Assert.Equal(["RedeliverAfterThrowScenarios.cs:2: await Task.Delay(TimeSpan.FromSeconds(2));"], violations);
    }

    [Fact]
    public void WaitersFileContainingTaskDelay_IsNotFlagged()
    {
        // Arrange — benign poll-loop pacing in a *Waiters helper is out of scope by
        // construction; only *Scenarios.cs files are held to the no-wall-clock invariant.
        const string source = """
            while (!probe.Saw)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
            """;

        // Act
        var violations = ConformanceScenarioWallClockBan.FindWallClockSleeps("OutboxProbeWaiters.cs", source);

        // Assert
        Assert.Empty(violations);
    }

    [Fact]
    public void ScenarioFileWithoutTaskDelay_IsNotFlagged()
    {
        // Arrange
        const string source = """
            await PumpUntilAsync(probe);
            Assert.True(probe.Saw);
            """;

        // Act
        var violations = ConformanceScenarioWallClockBan.FindWallClockSleeps("RedeliverAfterThrowScenarios.cs", source);

        // Assert
        Assert.Empty(violations);
    }

    [Fact]
    public void ScenarioFileWithMultipleTaskDelays_FlagsEachLine()
    {
        // Arrange
        const string source = """
            await Task.Delay(100);
            await PumpUntilAsync(probe);
            await Task.Delay(TimeSpan.FromSeconds(1));
            """;

        // Act
        var violations = ConformanceScenarioWallClockBan.FindWallClockSleeps("RedeliverAfterThrowScenarios.cs", source);

        // Assert
        Assert.Equal(
            [
                "RedeliverAfterThrowScenarios.cs:1: await Task.Delay(100);",
                "RedeliverAfterThrowScenarios.cs:3: await Task.Delay(TimeSpan.FromSeconds(1));",
            ],
            violations);
    }
}
