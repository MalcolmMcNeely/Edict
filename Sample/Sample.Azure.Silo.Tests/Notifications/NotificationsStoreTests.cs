using Sample.Domain.Orders.Notifications;

using Xunit;

namespace Sample.Azure.Silo.Tests.Notifications;

/// <summary>
/// The notifications sink is the in-process store the Web hosts behind its
/// append endpoint: the silo's <c>HttpEmailNotifier</c> POSTs a record per sent
/// notification, and the Orders view reads the same store back. This exercises
/// the store in isolation (no HTTP, no Web host): appended records come back
/// from the query in arrival order.
/// </summary>
public sealed class NotificationsStoreTests
{
    [Fact]
    public async Task Query_ReturnsAppendedRecords_InArrivalOrder()
    {
        // Arrange
        var store = new InMemoryNotificationsStore();
        var first = new SentNotification(
            OrderId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            EventId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var second = new SentNotification(
            OrderId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            EventId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        // Act
        store.Append(first);
        store.Append(second);

        // Assert
        await Verify(store.Query());
    }
}
