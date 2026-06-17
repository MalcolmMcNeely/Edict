using Edict.Mcp.Handlers;
using Edict.Mcp.Tools;
using Edict.Mcp.Tests.Versioning;

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
                Role: HandlerRole.ListProjectionBuilder,
                BoundContracts:
                [
                    new BoundContractInfo("Acme.Orders.OrderPlaced", "OrderId"),
                ],
                DeclaringAssembly: "Acme.Reporting",
                SourceLocation: null),
            new HandlerEntry(
                DeclaringTypeName: "Acme.Employees.EmployeeCommandHandler",
                Role: HandlerRole.CommandHandler,
                BoundContracts:
                [
                    new BoundContractInfo("Acme.Employees.AddEmployeeCommand", "EmployeeId", TenantScoped: true),
                ],
                DeclaringAssembly: "Acme.Employees",
                SourceLocation: null),
            new HandlerEntry(
                DeclaringTypeName: "Acme.Employees.EmployeeDirectoryProjection",
                Role: HandlerRole.ListProjectionBuilder,
                BoundContracts:
                [
                    new BoundContractInfo("Acme.Employees.EmployeeAdded", "EmployeeId", TenantScoped: true),
                ],
                DeclaringAssembly: "Acme.Employees",
                SourceLocation: null),
        ]);
        var tool = new ListRouteKeysTool(_ => Task.FromResult(inventory), StubVersionReportProvider.Clean());

        // Act
        var responseJson = await tool.InvokeAsync(arguments: null, CancellationToken.None);

        // Assert
        await Verify(responseJson);
    }
}
