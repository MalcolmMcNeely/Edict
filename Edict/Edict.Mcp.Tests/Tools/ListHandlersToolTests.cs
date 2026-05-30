using System.Text.Json;

using Edict.Mcp.Handlers;
using Edict.Mcp.Tools;

using static VerifyXunit.Verifier;

namespace Edict.Mcp.Tests.Tools;

public class ListHandlersToolTests
{
    [Fact]
    public async Task InvokeAsync_RendersInventoryAsStructuredJson()
    {
        // Arrange
        var inventory = new HandlerInventory(
        [
            new HandlerEntry(
                DeclaringTypeName: "Acme.Orders.OrderCommandHandler",
                Role: HandlerRole.CommandHandler,
                BoundContracts:
                [
                    new BoundContractInfo("Acme.Orders.PlaceOrderCommand", "OrderId"),
                    new BoundContractInfo("Acme.Orders.CancelOrderCommand", "OrderId"),
                ],
                DeclaringAssembly: "Acme.Orders",
                SourceLocation: new SourceLocationInfo("Acme.Orders/OrderCommandHandler.cs", 12, 5)),
            new HandlerEntry(
                DeclaringTypeName: "Acme.Notifications.OrderPlacedEmailHandler",
                Role: HandlerRole.EventHandler,
                BoundContracts:
                [
                    new BoundContractInfo("Acme.Notifications.OrderPlaced", "OrderId"),
                ],
                DeclaringAssembly: "Acme.Notifications",
                SourceLocation: new SourceLocationInfo("Acme.Notifications/OrderPlacedEmailHandler.cs", 8, 5)),
        ]);
        var tool = new ListHandlersTool(_ => Task.FromResult(inventory));

        // Act
        var responseJson = await tool.InvokeAsync(arguments: null, CancellationToken.None);

        // Assert
        await Verify(responseJson);
    }
}
