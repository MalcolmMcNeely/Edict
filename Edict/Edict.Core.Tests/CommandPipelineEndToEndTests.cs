using Edict.Abstractions;

namespace Edict.Core.Tests;

[Collection(EdictClusterCollection.Name)]
public sealed class CommandPipelineEndToEndTests(EdictClusterFixture fixture)
{
    [Fact]
    public async Task Send_routes_a_command_through_to_its_handler_and_returns_Accepted()
    {
        var result = await fixture.Sender.Send(
            new PlaceOrderCommand(Guid.NewGuid(), "SKU-1"));

        Assert.IsType<CommandResult.Accepted>(result);
    }

    [Fact]
    public async Task Send_returns_the_handlers_Rejected_outcome_with_its_reasons()
    {
        var result = await fixture.Sender.Send(
            new CancelOrderCommand(Guid.NewGuid(), "changed mind"));

        var rejected = Assert.IsType<CommandResult.Rejected>(result);
        var reason = Assert.Single(rejected.Reasons);
        Assert.Equal("already_shipped", reason.Code);
    }
}
