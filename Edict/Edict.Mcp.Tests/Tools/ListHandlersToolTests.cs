using System.Text.Json;

using Edict.Mcp.Handlers;
using Edict.Mcp.Tools;
using Edict.Mcp.Tests.Versioning;

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
            new HandlerEntry(
                DeclaringTypeName: "Acme.Shipping.ShipmentSaga",
                Role: HandlerRole.Saga,
                BoundContracts:
                [
                    new BoundContractInfo("Acme.Shipping.OrderPlaced", "OrderId"),
                ],
                DeclaringAssembly: "Acme.Shipping",
                SourceLocation: new SourceLocationInfo("Acme.Shipping/ShipmentSaga.cs", 6, 5),
                SagaTimeoutCap: SagaTimeoutCap.ForDuration("24:00:00")),
            new HandlerEntry(
                DeclaringTypeName: "Acme.Loans.LoanApplicationSaga",
                Role: HandlerRole.Saga,
                BoundContracts:
                [
                    new BoundContractInfo("Acme.Loans.ApplicationReceived", "ApplicationId"),
                ],
                DeclaringAssembly: "Acme.Loans",
                SourceLocation: new SourceLocationInfo("Acme.Loans/LoanApplicationSaga.cs", 9, 5),
                SagaTimeoutCap: SagaTimeoutCap.Unbounded),
            new HandlerEntry(
                DeclaringTypeName: "Acme.Returns.ReturnSaga",
                Role: HandlerRole.Saga,
                BoundContracts:
                [
                    new BoundContractInfo("Acme.Returns.ReturnRequested", "ReturnId"),
                ],
                DeclaringAssembly: "Acme.Returns",
                SourceLocation: new SourceLocationInfo("Acme.Returns/ReturnSaga.cs", 7, 5),
                SagaTimeoutCap: SagaTimeoutCap.InheritsDefault),
        ]);
        var tool = new ListHandlersTool(_ => Task.FromResult(inventory), StubVersionReportProvider.Clean());

        // Act
        var responseJson = await tool.InvokeAsync(arguments: null, CancellationToken.None);

        // Assert
        await Verify(responseJson);
    }
}
