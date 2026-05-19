using Edict.Contracts.Commands;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;

using Sample.Silo.Orders;

namespace Sample.Api.Orders;

internal static class OrdersEndpoints
{
    internal static void MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", PlaceOrder);
        app.MapPost("/orders/{id:guid}/lines", AddLineItem);
        app.MapPost("/orders/{id:guid}/submit", SubmitOrder);
        app.MapPost("/orders/{id:guid}/cancel", CancelOrder);
        app.MapGet("/orders/{id:guid}/projection", GetOrderProjection);
    }

    static async Task<IResult> PlaceOrder(IEdictSender sender)
    {
        var orderId = Guid.NewGuid();
        var result = await sender.Send(new PlaceOrderCommand(orderId));
        return MapResult(result, () => Results.Accepted(value: new { orderId }));
    }

    static async Task<IResult> AddLineItem(
        Guid id, AddLineItemRequest request, IEdictSender sender)
    {
        var result = await sender.Send(new AddLineItemCommand(id, request.Sku, request.Quantity));
        return MapResult(result, () => Results.Accepted());
    }

    static async Task<IResult> SubmitOrder(Guid id, IEdictSender sender)
    {
        var result = await sender.Send(new SubmitOrderCommand(id));
        return MapResult(result, () => Results.Accepted());
    }

    static async Task<IResult> CancelOrder(Guid id, IEdictSender sender)
    {
        var result = await sender.Send(new CancelOrderCommand(id));
        return MapResult(result, () => Results.Accepted());
    }

    static async Task<IResult> GetOrderProjection(
        Guid id, IEdictTableRepository<OrderStatusRow> repository)
    {
        var row = await repository.GetAsync(id.ToString(), "status");

        var statusCell = row is not null
            ? $"<td>{row.Status}</td><td>{row.ItemCount}</td>"
            : "<td colspan=\"2\">Not yet projected</td>";

        var html = $"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta http-equiv="refresh" content="2">
                <title>Order {id} — Projection</title>
            </head>
            <body>
                <h2>Order {id}</h2>
                <table border="1" cellpadding="4">
                    <tr><th>Status</th><th>Item Count</th></tr>
                    <tr>{statusCell}</tr>
                </table>
            </body>
            </html>
            """;

        return Results.Content(html, "text/html");
    }

    static IResult MapResult(EdictCommandResult result, Func<IResult> onAccepted) =>
        result switch
        {
            EdictCommandResult.Accepted => onAccepted(),
            EdictCommandResult.Rejected r => Results.UnprocessableEntity(r.Reasons),
            _ => throw new InvalidOperationException($"Unexpected result type: {result.GetType()}")
        };
}
