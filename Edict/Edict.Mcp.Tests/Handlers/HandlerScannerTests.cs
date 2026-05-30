using Edict.Mcp.Handlers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Edict.Mcp.Tests.Handlers;

public class HandlerScannerTests
{
    [Fact]
    public void Scan_EmptyCompilation_ReturnsEmptyInventory()
    {
        // Arrange
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        Assert.Empty(inventory.Handlers);
    }

    [Fact]
    public void Scan_DirectCommandHandlerSubclass_ReportsCommandHandlerEntryWithBoundCommandAndRouteKey()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Commands;
            using Edict.Contracts.Persistence;
            using Edict.Core.Commands;

            namespace Acme.Orders
            {
                public sealed record OrderState : IEdictPersistedState;

                public sealed record PlaceOrderCommand : EdictCommand
                {
                    [EdictRouteKey]
                    public System.Guid OrderId { get; init; }
                }

                public sealed partial class OrderCommandHandler : EdictCommandHandler<OrderState>
                {
                    public System.Threading.Tasks.Task HandleAsync(PlaceOrderCommand command) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.Equal("Acme.Orders.OrderCommandHandler", entry.DeclaringTypeName);
        Assert.Equal(HandlerRole.CommandHandler, entry.Role);
        Assert.Equal("ConsumerLibrary", entry.DeclaringAssembly);
        var bound = Assert.Single(entry.BoundContracts);
        Assert.Equal("Acme.Orders.PlaceOrderCommand", bound.FullTypeName);
        Assert.Equal("OrderId", bound.RouteKeyPropertyName);
    }

    [Fact]
    public void Scan_TransitiveInheritanceThroughGenericIntermediateBase_StillResolvesConcreteHandlerToCorrectRole()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Commands;
            using Edict.Contracts.Persistence;
            using Edict.Core.Commands;

            namespace Acme.Orders
            {
                public sealed record OrderState : IEdictPersistedState;

                public sealed record PlaceOrderCommand : EdictCommand
                {
                    [EdictRouteKey]
                    public System.Guid OrderId { get; init; }
                }

                public abstract class MyHandlerBase<TState> : EdictCommandHandler<TState>
                    where TState : IEdictPersistedState, new();

                public sealed partial class OrderCommandHandler : MyHandlerBase<OrderState>
                {
                    public System.Threading.Tasks.Task HandleAsync(PlaceOrderCommand command) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.Equal("Acme.Orders.OrderCommandHandler", entry.DeclaringTypeName);
        Assert.Equal(HandlerRole.CommandHandler, entry.Role);
        var bound = Assert.Single(entry.BoundContracts);
        Assert.Equal("Acme.Orders.PlaceOrderCommand", bound.FullTypeName);
    }

    [Fact]
    public void Scan_EventHandlerSubclass_ReportsEventHandlerEntryWithBoundEventAndRouteKey()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Core.EventHandler;

            namespace Acme.Notifications
            {
                [EdictStream("Orders")]
                public sealed record OrderPlaced : EdictEvent
                {
                    [EdictRouteKey]
                    public System.Guid OrderId { get; init; }
                }

                public sealed partial class OrderPlacedEmailHandler : EdictEventHandler
                {
                    public System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.Equal(HandlerRole.EventHandler, entry.Role);
        var bound = Assert.Single(entry.BoundContracts);
        Assert.Equal("Acme.Notifications.OrderPlaced", bound.FullTypeName);
        Assert.Equal("OrderId", bound.RouteKeyPropertyName);
    }

    [Fact]
    public void Scan_SagaSubclass_ReportsSagaRoleWithBoundEvent()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Sagas;

            namespace Acme.Shipping
            {
                public sealed record ShipmentProgress : IEdictPersistedState;

                [EdictStream("Orders")]
                public sealed record OrderPlaced : EdictEvent
                {
                    [EdictRouteKey]
                    public System.Guid OrderId { get; init; }
                }

                public sealed partial class ShipmentSaga : EdictSaga<ShipmentProgress>
                {
                    public System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.Equal("Acme.Shipping.ShipmentSaga", entry.DeclaringTypeName);
        Assert.Equal(HandlerRole.Saga, entry.Role);
    }

    [Fact]
    public void Scan_ProjectionBuilderAndTableProjectionBuilder_AreRecognisedAndDistinguishedByRole()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Projections;

            namespace Acme.Reporting
            {
                public sealed record OrdersByStatusRow : IEdictPersistedState;

                [EdictStream("Orders")]
                public sealed record OrderPlaced : EdictEvent;

                public sealed partial class OrderActivityProjection : EdictProjectionBuilder
                {
                    public System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
                }

                public sealed partial class OrdersByStatusProjection : EdictTableProjectionBuilder<OrdersByStatusRow>
                {
                    public System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var activity = Assert.Single(inventory.Handlers, entry => entry.DeclaringTypeName == "Acme.Reporting.OrderActivityProjection");
        var byStatus = Assert.Single(inventory.Handlers, entry => entry.DeclaringTypeName == "Acme.Reporting.OrdersByStatusProjection");
        Assert.Equal(HandlerRole.ProjectionBuilder, activity.Role);
        Assert.Equal(HandlerRole.TableProjectionBuilder, byStatus.Role);
    }

    [Fact]
    public void Scan_PartialHandlerSplitAcrossTwoFiles_StillProducesSingleEntry()
    {
        // Arrange
        const string handlerHalfA = """
            using Edict.Contracts.Commands;
            using Edict.Contracts.Persistence;
            using Edict.Core.Commands;

            namespace Acme.Orders
            {
                public sealed record OrderState : IEdictPersistedState;

                public sealed record PlaceOrderCommand : EdictCommand
                {
                    [EdictRouteKey]
                    public System.Guid OrderId { get; init; }
                }

                public sealed record CancelOrderCommand : EdictCommand
                {
                    [EdictRouteKey]
                    public System.Guid OrderId { get; init; }
                }

                public sealed partial class OrderCommandHandler : EdictCommandHandler<OrderState>
                {
                    public System.Threading.Tasks.Task HandleAsync(PlaceOrderCommand command) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        const string handlerHalfB = """
            namespace Acme.Orders
            {
                public sealed partial class OrderCommandHandler
                {
                    public System.Threading.Tasks.Task HandleAsync(CancelOrderCommand command) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, handlerHalfA, handlerHalfB);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.Equal("Acme.Orders.OrderCommandHandler", entry.DeclaringTypeName);
        Assert.Equal(2, entry.BoundContracts.Count);
        Assert.Contains(entry.BoundContracts, contract => contract.FullTypeName == "Acme.Orders.PlaceOrderCommand");
        Assert.Contains(entry.BoundContracts, contract => contract.FullTypeName == "Acme.Orders.CancelOrderCommand");
    }

    [Fact]
    public void Scan_FrameworkInternalAbstractBases_AreNotReported()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Persistence;
            using Edict.Core.Commands;

            namespace Acme.Shared
            {
                public sealed record OrderState : IEdictPersistedState;
                public abstract class TypedHandlerBase<TState> : EdictCommandHandler<TState>
                    where TState : IEdictPersistedState, new();
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        Assert.Empty(inventory.Handlers);
    }

    [Fact]
    public void Scan_SourceLocation_IsRelativeToSolutionDirectoryWithLineAndColumn()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Persistence;
            using Edict.Core.Commands;

            namespace Acme.Orders
            {
                public sealed record OrderState : IEdictPersistedState;
                public sealed partial class OrderCommandHandler : EdictCommandHandler<OrderState>;
            }
            """;
        var basesTree = CSharpSyntaxTree.ParseText(EdictBasesSource, path: @"C:/sln/ConsumerLibrary/EdictBases.cs");
        var consumerTree = CSharpSyntaxTree.ParseText(consumerSource, path: @"C:/sln/ConsumerLibrary/Orders/OrderCommandHandler.cs");
        var compilation = CreateCompilationFromTrees("ConsumerLibrary", basesTree, consumerTree);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: @"C:/sln");

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.NotNull(entry.SourceLocation);
        Assert.Equal("ConsumerLibrary/Orders/OrderCommandHandler.cs", entry.SourceLocation!.FilePath);
        Assert.Equal(7, entry.SourceLocation.Line);
    }

    static CSharpCompilation CreateCompilationFromTrees(string assemblyName, params SyntaxTree[] syntaxTrees)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Cast<MetadataReference>()
            .ToList();
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    static CSharpCompilation CreateCompilation(string assemblyName, params string[] sources)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Cast<MetadataReference>()
            .ToList();
        var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source));
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    const string EdictBasesSource = """
        using System;

        namespace Edict.Contracts.Persistence
        {
            public interface IEdictPersistedState { }
        }

        namespace Edict.Contracts.Commands
        {
            [AttributeUsage(AttributeTargets.Property)]
            public sealed class EdictRouteKeyAttribute : Attribute { }
            public abstract record EdictCommand;
        }

        namespace Edict.Contracts.Events
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class EdictStreamAttribute : Attribute
            {
                public EdictStreamAttribute(string name) { Name = name; }
                public string Name { get; }
            }
            public abstract record EdictEvent;
        }

        namespace Edict.Core.Commands
        {
            using Edict.Contracts.Persistence;
            public abstract class EdictCommandHandler<TState> where TState : IEdictPersistedState, new() { }
            public abstract class EdictCommandHandler : EdictCommandHandler<EdictUnit> { }
            public sealed class EdictUnit : IEdictPersistedState { }
        }

        namespace Edict.Core.EventHandler
        {
            public abstract class EdictEventHandler { }
        }

        namespace Edict.Core.Sagas
        {
            using Edict.Contracts.Persistence;
            public abstract class EdictSaga<TProgress> where TProgress : IEdictPersistedState, new() { }
        }

        namespace Edict.Core.Projections
        {
            using Edict.Contracts.Persistence;
            public abstract class EdictProjectionBuilder { }
            public abstract class EdictTableProjectionBuilder<T> : EdictProjectionBuilder where T : class, IEdictPersistedState, new() { }
        }
        """;
}
