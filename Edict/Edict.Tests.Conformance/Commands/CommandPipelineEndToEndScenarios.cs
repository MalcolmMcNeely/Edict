using Edict.Contracts.Commands;

using Xunit;

namespace Edict.Tests.Conformance.Commands;

/// <summary>
/// Substrate-agnostic conformance for the command pipeline:
/// <c>IEdictSender.Send</c> routes a command to its handler and surfaces the
/// outcome envelope (Accepted/Rejected) across the bound substrate. Each
/// provider's test project supplies a <see cref="ConformanceFixture"/> binding
/// and the inherited [Fact] methods run against the real substrate.
/// </summary>
public abstract class CommandPipelineEndToEndScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected CommandPipelineEndToEndScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Send_ShouldRouteCommandToHandlerAndReturnAccepted()
    {
        var result = await _fixture.Sender.Send(
            new PlaceOrderCommand(Guid.NewGuid(), "SKU-1"));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }

    [Fact]
    public async Task Send_ShouldReturnRejectedOutcomeWithReasons()
    {
        var result = await _fixture.Sender.Send(
            new CancelOrderCommand(Guid.NewGuid(), "changed mind"));

        var rejected = Assert.IsType<EdictCommandResult.Rejected>(result);
        var reason = Assert.Single(rejected.Reasons);
        Assert.Equal("already_shipped", reason.Code);
    }
}
