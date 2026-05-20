using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Silo.Orders;

using Xunit;

namespace Sample.Silo.Tests.Orders;

/// <summary>
/// Opts <see cref="OrderEmailHandler"/> deliveries into the chaos duplicate
/// redelivery (off-by-default for <c>EdictEventHandler</c> per ADR 0023 /
/// issue #67). The dedup ring (ADR 0002) must still suppress the duplicate so
/// exactly one <c>Invocation</c> entry surfaces on the timeline — proving the
/// chaos-for-invocations opt-in is wired and that the dedup guarantee holds
/// under it.
/// </summary>
public sealed class OrderEmailHandlerChaosTests
{
    [Fact]
    public async Task ChaosForInvocations_ShouldRecordOneInvocation_WhenDedupRingSuppressesDuplicate()
    {
        var orderId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithChaosForInvocations());

        await app.Send(new PlaceOrderCommand(orderId));
        await app.Drain();

        await Verify(app.Timeline);
    }
}
