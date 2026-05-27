using Sample.Domain.Orders.CommandHandlers;
using Sample.Web.Components.Simulator;

using Xunit;

namespace Edict.Architecture.Tests;

// The Sample restructure (issue #137) extracts substrate-agnostic Sample.Silo
// code into a Sample.Domain class library and substrate-agnostic Sample.Web
// code into a Sample.Web.Components Razor class library, so future Kafka/Postgres
// samples can reuse both. The assertions here lock in that extraction:
// handlers/sagas/projection builders/state must ship from Sample.Domain;
// pages/layouts/simulator/state must ship from Sample.Web.Components. The two
// Sample.* host projects (Sample.Silo, Sample.Web) keep only their substrate-
// specific Program.cs.
public class SampleRestructureTests
{
    [Fact]
    public void OrderCommandHandler_ShouldResideInSampleDomain()
    {
        Assert.Equal("Sample.Domain", typeof(OrderCommandHandler).Assembly.GetName().Name);
    }

    [Fact]
    public void IDeterministicOrderPlacer_ShouldResideInSampleWebComponents()
    {
        Assert.Equal("Sample.Web.Components", typeof(IDeterministicOrderPlacer).Assembly.GetName().Name);
    }
}
