using Edict.Contracts.Commands;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance for the command pipeline: <c>IEdictSender.Send</c>
/// routes a command to its handler and surfaces the outcome envelope
/// (Accepted/Rejected) over the bound streaming provider. The Accepted path rides
/// the publish path; the reference persistence holds the idempotency state.
/// </summary>
public abstract class CommandPipelineEndToEndScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected CommandPipelineEndToEndScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Send_ShouldRouteCommandToHandlerAndReturnAccepted()
    {
        var result = await _fixture.Sender.SendAsync(new PlaceOrderCommand(Guid.NewGuid(), "SKU-1"));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }

    [Fact]
    public async Task Send_ShouldReturnRejectedOutcomeWithReasons()
    {
        var result = await _fixture.Sender.SendAsync(new CancelOrderCommand(Guid.NewGuid(), "changed mind"));

        var rejected = Assert.IsType<EdictCommandResult.Rejected>(result);
        var reason = Assert.Single(rejected.Reasons);
        Assert.Equal("already_shipped", reason.Code);
    }
}
