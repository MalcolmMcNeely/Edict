namespace Sample.Domain.Orders.Notifications;

/// <summary>
/// The notifications sink: a host-singleton store the Web exposes behind its
/// append endpoint and reads back in-process for the Orders view. The silo
/// reaches it only over HTTP, modelling the Event Handler calling out to an
/// external notifications system.
/// </summary>
public interface INotificationsStore
{
    void Append(SentNotification notification);

    IReadOnlyList<SentNotification> Query();
}
