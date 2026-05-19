using Edict.Contracts.Commands;

namespace Edict.Core.Tests.Grains;

[Collection(EdictClusterCollection.Name)]
public sealed class CommandPipelineEndToEndTests(EdictClusterFixture fixture)
{
    [Fact]
    public async Task Send_ShouldRouteCommandToHandlerAndReturnAccepted()
    {
        var result = await fixture.Sender.Send(
            new PlaceOrderCommand(Guid.NewGuid(), "SKU-1"));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }

    [Fact]
    public async Task Send_ShouldReturnRejectedOutcomeWithReasons()
    {
        var result = await fixture.Sender.Send(
            new CancelOrderCommand(Guid.NewGuid(), "changed mind"));

        var rejected = Assert.IsType<EdictCommandResult.Rejected>(result);
        var reason = Assert.Single(rejected.Reasons);
        Assert.Equal("already_shipped", reason.Code);
    }
}
