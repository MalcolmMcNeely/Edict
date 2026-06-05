using Edict.Core.Projections;

namespace Edict.Core.Tests.Projections;

// A stand-in for a read-model row. The resolver only ever uses it as a Type key,
// so a plain marker keeps this a pure unit test (no persisted-state contract).
file sealed class OrderStatusRow;

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
    public void Resolve_ShouldThrowEdictUnreadableProjectionException_WhenRowTypeIsUnmapped()
    {
        var resolver = new ProjectionReadRouteResolver(new Dictionary<Type, string>());

        var exception = Assert.Throws<EdictUnreadableProjectionException>(
            () => resolver.Resolve(typeof(OrderStatusRow)));

        Assert.Equal(typeof(OrderStatusRow), exception.RowType);
    }
}
