using Microsoft.Extensions.Logging;

namespace Sample.Domain.Fulfillment;

/// <summary>
/// Default <see cref="IWarehouseGateway"/> for the Sample app: logs a single
/// structured line per dispatched line item. No real warehouse integration — the
/// Sample stays self-contained per <c>CONTEXT.md</c>.
/// </summary>
public sealed class LoggingWarehouseGateway(ILogger<LoggingWarehouseGateway> logger) : IWarehouseGateway
{
    public Task DispatchLineAsync(Guid orderId, Guid lineItemId)
    {
        logger.LogInformation(
            "Simulated warehouse dispatch for line {LineItemId} of order {OrderId}.",
            lineItemId,
            orderId);
        return Task.CompletedTask;
    }
}
