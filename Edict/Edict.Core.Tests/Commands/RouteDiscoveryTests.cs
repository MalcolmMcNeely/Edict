using System.Reflection;
using System.Reflection.Emit;

using Edict.Contracts.Commands;
using Edict.Contracts.Routing;
using Edict.Core.Commands;
using Edict.Core.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edict.Core.Tests.Commands;

public class RouteDiscoveryTests
{
    public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
    {
        [EdictRouteKey]
        public Guid OrderId { get; init; } = OrderId;
    }

    public sealed partial record CancelOrder(Guid OrderId) : EdictCommand
    {
        [EdictRouteKey]
        public Guid OrderId { get; init; } = OrderId;
    }

    public interface IOrderHandler;

    public interface ICancelHandler;

    public static class PlaceOrderRegistrar
    {
        public static void Register(Dictionary<Type, CommandRoute> routes) =>
            routes[typeof(PlaceOrder)] = new CommandRoute(
                typeof(PlaceOrder),
                typeof(IOrderHandler),
                "RouteDiscoveryTests+OrderHandler",
                c => ((PlaceOrder)c).OrderId);
    }

    public static class CancelOrderRegistrar
    {
        public static void Register(Dictionary<Type, CommandRoute> routes) =>
            routes[typeof(CancelOrder)] = new CommandRoute(
                typeof(CancelOrder),
                typeof(ICancelHandler),
                "RouteDiscoveryTests+CancelHandler",
                c => ((CancelOrder)c).OrderId);
    }

    static Assembly BuildAssemblyWithRoutes(Type registrarType, string name)
    {
        var ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
        var ctor = typeof(EdictRoutesAttribute).GetConstructor([typeof(Type)])!;
        ab.SetCustomAttribute(new CustomAttributeBuilder(ctor, [registrarType]));
        return ab;
    }

    static Assembly BuildAssemblyWithoutRoutes(string name) =>
        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);

    [Fact]
    public void RouteDiscovery_ShouldBuildDictionary_WhenSingleAssemblyHasRoutes()
    {
        var asm = BuildAssemblyWithRoutes(typeof(PlaceOrderRegistrar), nameof(RouteDiscovery_ShouldBuildDictionary_WhenSingleAssemblyHasRoutes));

        var routes = RouteDiscovery.Discover([asm], requireAttribute: false, NullLogger.Instance);

        Assert.True(routes.ContainsKey(typeof(PlaceOrder)));
        Assert.Equal(typeof(IOrderHandler), routes[typeof(PlaceOrder)].GrainInterfaceType);
    }

    sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    public static class DuplicatePlaceOrderRegistrar
    {
        public static void Register(Dictionary<Type, CommandRoute> routes) =>
            routes[typeof(PlaceOrder)] = new CommandRoute(
                typeof(PlaceOrder),
                typeof(IOrderHandler),
                "RouteDiscoveryTests+OtherHandler",
                c => ((PlaceOrder)c).OrderId);
    }

    [Fact]
    public void RouteDiscovery_ShouldThrow_WhenSameCommandTypeRegisteredInTwoAssemblies()
    {
        var asmA = BuildAssemblyWithRoutes(typeof(PlaceOrderRegistrar), nameof(RouteDiscovery_ShouldThrow_WhenSameCommandTypeRegisteredInTwoAssemblies) + "A");
        var asmB = BuildAssemblyWithRoutes(typeof(DuplicatePlaceOrderRegistrar), nameof(RouteDiscovery_ShouldThrow_WhenSameCommandTypeRegisteredInTwoAssemblies) + "B");

        var exception = Assert.Throws<EdictWiringException>(
            () => RouteDiscovery.Discover([asmA, asmB], requireAttribute: false, NullLogger.Instance));

        Assert.Contains(typeof(PlaceOrder).FullName!, exception.Message);
        Assert.Contains(asmA.GetName().Name!, exception.Message);
        Assert.Contains(asmB.GetName().Name!, exception.Message);
    }

    [Fact]
    public void RouteDiscovery_ShouldThrow_WhenExplicitAssemblyMissingRoutesAttribute()
    {
        var asm = BuildAssemblyWithoutRoutes(nameof(RouteDiscovery_ShouldThrow_WhenExplicitAssemblyMissingRoutesAttribute));

        var exception = Assert.Throws<EdictWiringException>(
            () => RouteDiscovery.Discover([asm], requireAttribute: true, NullLogger.Instance));

        Assert.Contains(asm.GetName().Name!, exception.Message);
        Assert.Contains(nameof(EdictRoutesAttribute), exception.Message);
    }

    [Fact]
    public void RouteDiscovery_ShouldLogWarningAndReturnEmpty_WhenNoAssemblyHasRoutes()
    {
        var asm = BuildAssemblyWithoutRoutes(nameof(RouteDiscovery_ShouldLogWarningAndReturnEmpty_WhenNoAssemblyHasRoutes));
        var logger = new CapturingLogger();

        var routes = RouteDiscovery.Discover([asm], requireAttribute: false, logger);

        Assert.Empty(routes);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void RouteDiscovery_ShouldMergeEntries_WhenMultipleAssembliesHaveRoutes()
    {
        var asmA = BuildAssemblyWithRoutes(typeof(PlaceOrderRegistrar), nameof(RouteDiscovery_ShouldMergeEntries_WhenMultipleAssembliesHaveRoutes) + "A");
        var asmB = BuildAssemblyWithRoutes(typeof(CancelOrderRegistrar), nameof(RouteDiscovery_ShouldMergeEntries_WhenMultipleAssembliesHaveRoutes) + "B");

        var routes = RouteDiscovery.Discover([asmA, asmB], requireAttribute: false, NullLogger.Instance);

        Assert.True(routes.ContainsKey(typeof(PlaceOrder)));
        Assert.True(routes.ContainsKey(typeof(CancelOrder)));
    }
}
