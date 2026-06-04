using Xunit;

namespace Edict.Architecture.Tests;

public class ConformanceBindingCompletenessMatcherTests
{
    static readonly HashSet<string> Battery = ["AlphaScenarios", "BetaScenarios", "GammaScenarios"];

    [Fact]
    public void EveryProviderBindingTheFullBattery_LeavesNoGaps()
    {
        // Arrange
        var bindings = new Dictionary<string, IReadOnlySet<string>>
        {
            ["Azure"] = new HashSet<string>(Battery),
            ["Kafka"] = new HashSet<string>(Battery),
        };

        // Act
        var gaps = ConformanceBindingCompleteness.FindBindingGaps(Battery, bindings);

        // Assert
        Assert.Empty(gaps);
    }

    [Fact]
    public void ProviderMissingOneScenario_ReportsExactlyThatGap()
    {
        // Arrange
        var bindings = new Dictionary<string, IReadOnlySet<string>>
        {
            ["Kafka"] = new HashSet<string> { "AlphaScenarios", "GammaScenarios" },
        };

        // Act
        var gaps = ConformanceBindingCompleteness.FindBindingGaps(Battery, bindings);

        // Assert
        Assert.Equal(["Kafka does not bind battery scenario BetaScenarios"], gaps);
    }

    [Fact]
    public void ProviderBindingAScenarioFromAnotherBattery_ReportsItAsWrongBattery()
    {
        // Arrange — DeltaScenarios belongs to the other axis; binding it here is a
        // provider in the wrong battery.
        var bindings = new Dictionary<string, IReadOnlySet<string>>
        {
            ["Azure"] = new HashSet<string> { "AlphaScenarios", "BetaScenarios", "GammaScenarios", "DeltaScenarios" },
        };

        // Act
        var gaps = ConformanceBindingCompleteness.FindBindingGaps(Battery, bindings);

        // Assert
        Assert.Equal(["Azure binds DeltaScenarios, which is not in this battery"], gaps);
    }

    [Fact]
    public void ScenarioNameThatIsASubstringOfABatteryScenario_DoesNotCountAsBindingIt()
    {
        // Arrange — "AlphaScenarios" is a substring of the battery's
        // "AlphaExtendedScenarios", but exact matching means binding the shorter
        // name neither satisfies the longer one nor passes as in-battery.
        var battery = new HashSet<string> { "AlphaExtendedScenarios" };
        var bindings = new Dictionary<string, IReadOnlySet<string>>
        {
            ["Azure"] = new HashSet<string> { "AlphaScenarios" },
        };

        // Act
        var gaps = ConformanceBindingCompleteness.FindBindingGaps(battery, bindings);

        // Assert
        Assert.Equal(
            [
                "Azure binds AlphaScenarios, which is not in this battery",
                "Azure does not bind battery scenario AlphaExtendedScenarios",
            ],
            gaps.OrderBy(gap => gap, StringComparer.Ordinal));
    }
}
