using Edict.Contracts.Commands;

namespace Edict.Azure.Tests.Commands;

/// <summary>
/// Azurite/Testcontainers conformance for the command pipeline:
/// <c>IEdictSender.Send</c> routes a command to its handler and surfaces the
/// outcome envelope (Accepted/Rejected) across the real Azure-substrate
/// cluster. Lifted from <c>CommandPipelineEndToEndTests</c> in Core.Tests so
/// the proof exercises the same Azure Queue + Azure Blob substrate the sample
/// silo wires in production.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class CommandPipelineEndToEndTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task Send_ShouldRouteCommandToHandlerAndReturnAccepted()
    {
        var result = await fixture.Sender.Send(
            new AzurePlaceOrderCommand(Guid.NewGuid(), "SKU-1"));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }

    [Fact]
    public async Task Send_ShouldReturnRejectedOutcomeWithReasons()
    {
        var result = await fixture.Sender.Send(
            new AzureCancelOrderCommand(Guid.NewGuid(), "changed mind"));

        var rejected = Assert.IsType<EdictCommandResult.Rejected>(result);
        var reason = Assert.Single(rejected.Reasons);
        Assert.Equal("already_shipped", reason.Code);
    }
}
