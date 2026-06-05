namespace Sample.Domain.Orders.Notifications;

/// <summary>
/// Thread-safe in-memory <see cref="INotificationsStore"/>. The Web hosts a
/// single instance; the append endpoint and the Orders view race on it, so
/// reads and writes are guarded and <see cref="Query"/> returns a snapshot the
/// caller cannot mutate. Self-contained, no external store, per <c>CONTEXT.md</c>.
/// </summary>
public sealed class InMemoryNotificationsStore : INotificationsStore
{
    readonly Lock _gate = new();
    readonly List<SentNotification> _notifications = new();

    public void Append(SentNotification notification)
    {
        lock (_gate)
        {
            _notifications.Add(notification);
        }
    }

    public IReadOnlyList<SentNotification> Query()
    {
        lock (_gate)
        {
            return _notifications.ToArray();
        }
    }
}
