using Edict.Contracts.Results;
using Edict.Contracts.Sending;
using Sample.Orders;

namespace Sample.Api.Orders;

internal static class OrdersEndpoints
{
    internal static void MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", PlaceOrder);
        app.MapPost("/orders/{id:guid}/lines", AddLineItem);
        app.MapPost("/orders/{id:guid}/submit", SubmitOrder);
        app.MapPost("/orders/{id:guid}/cancel", CancelOrder);
    }

    private static async Task<IResult> PlaceOrder(IEdictSender sender)
    {
        var orderId = Guid.NewGuid();
        var result = await sender.Send(new PlaceOrderCommand(orderId));
        return MapResult(result, () => Results.Accepted(value: new { orderId }));
    }

    private static async Task<IResult> AddLineItem(
        Guid id, AddLineItemRequest request, IEdictSender sender)
    {
        var result = await sender.Send(new AddLineItemCommand(id, request.Sku, request.Quantity));
        return MapResult(result, () => Results.Accepted());
    }

    private static async Task<IResult> SubmitOrder(Guid id, IEdictSender sender)
    {
        var result = await sender.Send(new SubmitOrderCommand(id));
        return MapResult(result, () => Results.Accepted());
    }

    private static async Task<IResult> CancelOrder(Guid id, IEdictSender sender)
    {
        var result = await sender.Send(new CancelOrderCommand(id));
        return MapResult(result, () => Results.Accepted());
    }

    private static IResult MapResult(CommandResult result, Func<IResult> onAccepted) =>
        result switch
        {
            CommandResult.Accepted => onAccepted(),
            CommandResult.Rejected r => Results.UnprocessableEntity(r.Reasons),
            _ => throw new InvalidOperationException($"Unexpected result type: {result.GetType()}")
        };
}
