using Edict.Core.Projections;

namespace Edict.Core.Tests.Projections;

// Stand-ins for the two species' read-model types. The resolver only ever uses
// them as Type keys, so plain markers keep this a pure unit test (no
// persisted-state contract): OrderStatusRow stands for a List projection's row,
// DeliveryStatus for an in-grain projection's payload.
file sealed class OrderStatusRow;

file sealed class DeliveryStatus;

public class ProjectionReadRouteResolverTests
{
    [Fact]
    public void Resolve_ShouldReturnMappedGrainClassName()
    {
        var resolver = new ProjectionReadRouteResolver(
            new Dictionary<Type, string> { [typeof(OrderStatusRow)] = "OrderProjectionBuilder" });

        var grainClassName = resolver.Resolve(typeof(OrderStatusRow));

        Assert.Equal("OrderProjectionBuilder", grainClassName);
    }

    [Fact]
    public void Resolve_ShouldResolveBothSpecies_FromTheOneMap()
    {
        var resolver = new ProjectionReadRouteResolver(new Dictionary<Type, string>
        {
            [typeof(OrderStatusRow)] = "OrdersByStatusProjectionBuilder",
            [typeof(DeliveryStatus)] = "DeliveryStatusProjectionBuilder",
        });

        Assert.Equal("OrdersByStatusProjectionBuilder", resolver.Resolve(typeof(OrderStatusRow)));
        Assert.Equal("DeliveryStatusProjectionBuilder", resolver.Resolve(typeof(DeliveryStatus)));
    }

    [Fact]
    public void Resolve_ShouldThrowEdictUnreadableProjectionException_WhenRowTypeIsUnmapped()
    {
        var resolver = new ProjectionReadRouteResolver(new Dictionary<Type, string>());

        var exception = Assert.Throws<EdictUnreadableProjectionException>(
            () => resolver.Resolve(typeof(OrderStatusRow)));

        Assert.Equal(typeof(OrderStatusRow), exception.RowType);
    }
}
