using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Domain.Orders;
using Sample.Domain.Orders.CommandHandlers;
using Sample.Domain.Orders.EventHandlers;

using Xunit;

namespace Sample.Silo.Tests.Orders;

/// <summary>
/// Exercises <c>EdictTestAppBuilder.Replace&lt;TService&gt;</c>: a fake
/// <see cref="IEmailNotifier"/> handed to the builder must win on the silo
/// container so <see cref="OrderEmailEventHandler"/>'s deferred invocation routes
/// through it. Proves the new fake-injection seam is wired through both the
/// silo registration path and the harness's last-AddSingleton-wins ordering.
/// </summary>
public sealed class OrderEmailHandlerReplaceTests
{
    [Fact]
    public async Task Replace_ShouldRouteEmailNotifierCalls_ToTheFake()
    {
        var orderId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var fake = new RecordingEmailNotifier();

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .Replace<IEmailNotifier>(fake));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Drain();

        Assert.Single(fake.SentOrderIds);
        Assert.Equal(orderId, fake.SentOrderIds[0]);
    }

    sealed class RecordingEmailNotifier : IEmailNotifier
    {
        public List<Guid> SentOrderIds { get; } = new();

        public Task SendOrderPlacedAsync(Guid orderId, Guid eventId)
        {
            SentOrderIds.Add(orderId);
            return Task.CompletedTask;
        }
    }
}
