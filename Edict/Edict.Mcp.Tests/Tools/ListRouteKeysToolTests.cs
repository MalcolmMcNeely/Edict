using Edict.Mcp.Handlers;
using Edict.Mcp.Tools;

using static VerifyXunit.Verifier;

namespace Edict.Mcp.Tests.Tools;

public class ListRouteKeysToolTests
{
    [Fact]
    public async Task InvokeAsync_GroupsHandlersByContractAndFlagsCommandCollisions()
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
                ],
                DeclaringAssembly: "Acme.Orders",
                SourceLocation: null),
            new HandlerEntry(
                DeclaringTypeName: "Acme.Orders.RogueOrderCommandHandler",
                Role: HandlerRole.CommandHandler,
                BoundContracts:
                [
                    new BoundContractInfo("Acme.Orders.PlaceOrderCommand", "OrderId"),
                ],
                DeclaringAssembly: "Acme.Orders",
                SourceLocation: null),
            new HandlerEntry(
                DeclaringTypeName: "Acme.Notifications.OrderPlacedEmailHandler",
                Role: HandlerRole.EventHandler,
                BoundContracts:
                [
                    new BoundContractInfo("Acme.Orders.OrderPlaced", "OrderId"),
                ],
                DeclaringAssembly: "Acme.Notifications",
                SourceLocation: null),
            new HandlerEntry(
                DeclaringTypeName: "Acme.Reporting.OrdersByStatusProjection",
                Role: HandlerRole.TableProjectionBuilder,
                BoundContracts:
                [
                    new BoundContractInfo("Acme.Orders.OrderPlaced", "OrderId"),
                ],
                DeclaringAssembly: "Acme.Reporting",
                SourceLocation: null),
        ]);
        var tool = new ListRouteKeysTool(_ => Task.FromResult(inventory));

        // Act
        var responseJson = await tool.InvokeAsync(arguments: null, CancellationToken.None);

        // Assert
        await Verify(responseJson);
    }
}
