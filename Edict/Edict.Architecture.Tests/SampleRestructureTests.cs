using Sample.Domain.Orders.CommandHandlers;

using Xunit;

namespace Edict.Architecture.Tests;

// The Sample restructure (issue #137) extracts substrate-agnostic Sample.Silo
// code into a Sample.Domain class library so future Kafka/Postgres samples can
// reuse it. The assertions here lock in that extraction: handlers/sagas/projection
// builders/state must ship from Sample.Domain, not Sample.Silo (whose only role
// after the split is the Azure-specific Program.cs host).
public class SampleRestructureTests
{
    [Fact]
    public void OrderCommandHandler_ShouldResideInSampleDomain()
    {
        Assert.Equal("Sample.Domain", typeof(OrderCommandHandler).Assembly.GetName().Name);
    }
}
