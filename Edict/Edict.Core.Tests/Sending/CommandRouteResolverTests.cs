using Edict.Contracts.Commands;
using Edict.Core.Sending;

namespace Edict.Core.Tests.Sending;

file sealed record PlaceOrder(Guid OrderId) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

// A stand-in for the generated aggregate grain interface. The resolver only
// ever stores and returns this as a Type, so a plain marker (deliberately not
// an Orleans grain interface) keeps this a pure unit test.
file interface IOrderGrain;

public class CommandRouteResolverTests
{
    private static CommandRouteResolver ResolverFor(params CommandRoute[] routes) =>
        new(routes.ToDictionary(route => route.CommandType));

    [Fact]
    public void Resolve_returns_the_mapped_grain_interface_and_the_RouteKey_value()
    {
        var orderId = Guid.NewGuid();
        var resolver = ResolverFor(
            new CommandRoute(typeof(PlaceOrder), typeof(IOrderGrain), "OrderGrain",
                command => ((PlaceOrder)command).OrderId));

        var (grainInterfaceType, key) = resolver.Resolve(new PlaceOrder(orderId));

        Assert.Equal(typeof(IOrderGrain), grainInterfaceType);
        Assert.Equal(orderId, key);
    }

    [Fact]
    public void Resolve_passes_an_empty_RouteKey_through_unchanged()
    {
        var resolver = ResolverFor(
            new CommandRoute(typeof(PlaceOrder), typeof(IOrderGrain), "OrderGrain",
                command => ((PlaceOrder)command).OrderId));

        var (_, key) = resolver.Resolve(new PlaceOrder(Guid.Empty));

        Assert.Equal(Guid.Empty, key);
    }

    [Fact]
    public void Resolve_throws_an_unroutable_command_exception_for_an_unmapped_command()
    {
        var resolver = ResolverFor();

        var exception = Assert.Throws<UnroutableCommandException>(
            () => resolver.Resolve(new PlaceOrder(Guid.NewGuid())));

        Assert.Equal(typeof(PlaceOrder), exception.CommandType);
    }
}
