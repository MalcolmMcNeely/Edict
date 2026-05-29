using Edict.Contracts.Commands;
using Edict.Core.Commands;

namespace Edict.Core.Tests.Sending;

file sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

// A stand-in for the generated aggregate grain interface. The resolver only
// ever stores and returns this as a Type, so a plain marker (deliberately not
// an Orleans grain interface) keeps this a pure unit test.
file interface IOrderCommandHandler;

public class CommandRouteResolverTests
{
    static CommandRouteResolver ResolverFor(params CommandRoute[] routes) =>
        new(routes.ToDictionary(route => route.CommandType));

    [Fact]
    public void Resolve_ShouldReturnMappedGrainInterfaceAndRouteKeyValue()
    {
        var orderId = Guid.NewGuid();
        var resolver = ResolverFor(
            new CommandRoute(typeof(PlaceOrder), typeof(IOrderCommandHandler), "OrderCommandHandler",
                command => ((PlaceOrder)command).OrderId));

        var (grainInterfaceType, key) = resolver.Resolve(new PlaceOrder(orderId));

        Assert.Equal(typeof(IOrderCommandHandler), grainInterfaceType);
        Assert.Equal(orderId, key);
    }

    [Fact]
    public void Resolve_ShouldPassEmptyRouteKeyThroughUnchanged()
    {
        var resolver = ResolverFor(
            new CommandRoute(typeof(PlaceOrder), typeof(IOrderCommandHandler), "OrderCommandHandler",
                command => ((PlaceOrder)command).OrderId));

        var (_, key) = resolver.Resolve(new PlaceOrder(Guid.Empty));

        Assert.Equal(Guid.Empty, key);
    }

    [Fact]
    public void Resolve_ShouldThrowUnroutableCommandException_WhenCommandIsUnmapped()
    {
        var resolver = ResolverFor();

        var exception = Assert.Throws<UnroutableCommandException>(
            () => resolver.Resolve(new PlaceOrder(Guid.NewGuid())));

        Assert.Equal(typeof(PlaceOrder), exception.CommandType);
    }
}
