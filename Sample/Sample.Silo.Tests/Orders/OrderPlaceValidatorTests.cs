using Edict.Contracts.Commands;
using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Silo.Orders.CommandHandlers;

using Xunit;

namespace Sample.Silo.Tests.Orders;

public sealed class OrderPlaceValidatorTests
{
    [Fact]
    public async Task Validator_ShouldReturnRejectedWithMappedReason_WhenCustomerReferenceIsEmpty()
    {
        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        var result = await app.Send(new PlaceOrderCommand(Guid.NewGuid(), string.Empty));

        var rejected = Assert.IsType<EdictCommandResult.Rejected>(result);
        var reason = Assert.Single(rejected.Reasons);
        Assert.Equal("customer_reference_required", reason.Code);
    }

    [Fact]
    public async Task Validator_ShouldAllowHandleToRunAndReturnAccepted_WhenCustomerReferenceIsPresent()
    {
        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        var result = await app.Send(new PlaceOrderCommand(Guid.NewGuid(), "REF-001"));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }
}
