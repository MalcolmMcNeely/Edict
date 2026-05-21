using Sample.Silo.Orders.EventHandlers;

namespace Sample.Silo.Orders;

/// <summary>
/// Consumer-injected collaborator the <see cref="OrderEmailEventHandler"/> delegates
/// to when an order is placed. The Sample app's default implementation just
/// logs the simulated send; the seam exists so tests can fake-inject a
/// recording substitute via <c>EdictTestAppBuilder.Replace&lt;IEmailNotifier&gt;</c>.
/// </summary>
public interface IEmailNotifier
{
    Task SendOrderPlacedAsync(Guid orderId, Guid eventId);
}
