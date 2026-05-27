using Edict.Benchmarks.Throughput.ClosedLoop;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class ClosedLoopScenarioNameTests
{
    [Fact]
    public void CommandsScenario_Name_ShouldReadCommandAcceptance()
    {
        var scenario = new CommandsScenario(sender: null!);

        Assert.Equal("Command acceptance", scenario.Name);
    }

    [Fact]
    public void EventsScenario_Name_ShouldReadCommandToEventDelivery()
    {
        var scenario = new EventsScenario(sender: null!, rowRepository: null!);

        Assert.Equal("Command → Event delivery", scenario.Name);
    }
}
