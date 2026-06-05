using Sample.Domain.Fulfillment.CommandHandlers;

namespace Sample.Domain.Fulfillment;

/// <summary>
/// Consumer-injected collaborator the <see cref="FulfillmentCommandHandler"/>
/// delegates to as each line is fulfilled — the seam to a real warehouse or
/// carrier that picks and dispatches the line. The Sample app's default
/// implementation just logs the simulated dispatch; the seam exists so tests can
/// fake-inject a recording substitute via
/// <c>EdictTestAppBuilder.Replace&lt;IWarehouseGateway&gt;</c>, proving a schedule
/// fire handler composes with dependency injection.
/// </summary>
public interface IWarehouseGateway
{
    Task DispatchLineAsync(Guid orderId, Guid lineItemId);
}
