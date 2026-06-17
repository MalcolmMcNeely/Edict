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
                    System.Threading.Tasks.Task HandleAsync(PlaceOrderCommand command) => System.Threading.Tasks.Task.CompletedTask;
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
    public void Scan_TenantScopedRouteKeyType_FlagsBoundContractAsTenantScoped()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Commands;
            using Edict.Contracts.Persistence;
            using Edict.Contracts.Tenancy;
            using Edict.Core.Commands;

            namespace Acme.Employees
            {
                public sealed record EmployeeState : IEdictPersistedState;

                [EdictTenantScoped]
                public readonly record struct EmployeeId(System.Guid Value);

                public sealed record AddEmployeeCommand : EdictCommand
                {
                    [EdictRouteKey]
                    public EmployeeId EmployeeId { get; init; }
                }

                public sealed partial class EmployeeCommandHandler : EdictCommandHandler<EmployeeState>
                {
                    System.Threading.Tasks.Task HandleAsync(AddEmployeeCommand command) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        var bound = Assert.Single(entry.BoundContracts);
        Assert.Equal("Acme.Employees.AddEmployeeCommand", bound.FullTypeName);
        Assert.Equal("EmployeeId", bound.RouteKeyPropertyName);
        Assert.True(bound.TenantScoped);
    }

    [Fact]
    public void Scan_PublicRouteKeyType_LeavesBoundContractNotTenantScoped()
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
                    System.Threading.Tasks.Task HandleAsync(PlaceOrderCommand command) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        var bound = Assert.Single(entry.BoundContracts);
        Assert.False(bound.TenantScoped);
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
                    System.Threading.Tasks.Task HandleAsync(PlaceOrderCommand command) => System.Threading.Tasks.Task.CompletedTask;
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
                    System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
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
                    System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
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
    public void Scan_SagaWithTimeoutAttribute_SurfacesDurationCap()
    {
        // Arrange
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, SagaSource("[EdictSagaTimeout(\"24:00:00\")]"));
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.Equal(HandlerRole.Saga, entry.Role);
        Assert.NotNull(entry.SagaTimeoutCap);
        Assert.Equal(SagaTimeoutCapKind.Duration, entry.SagaTimeoutCap!.Kind);
        Assert.Equal("24:00:00", entry.SagaTimeoutCap.Duration);
    }

    [Fact]
    public void Scan_SagaWithUnboundedTimeoutAttribute_SurfacesUnboundedCap()
    {
        // Arrange
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, SagaSource("[EdictSagaTimeout(Unbounded = true)]"));
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.NotNull(entry.SagaTimeoutCap);
        Assert.Equal(SagaTimeoutCapKind.Unbounded, entry.SagaTimeoutCap!.Kind);
        Assert.Null(entry.SagaTimeoutCap.Duration);
    }

    [Fact]
    public void Scan_SagaWithoutTimeoutAttribute_SurfacesDefaultCap()
    {
        // Arrange
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, SagaSource(attribute: ""));
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.NotNull(entry.SagaTimeoutCap);
        Assert.Equal(SagaTimeoutCapKind.Default, entry.SagaTimeoutCap!.Kind);
        Assert.Null(entry.SagaTimeoutCap.Duration);
    }

    [Fact]
    public void Scan_NonSagaHandler_HasNoTimeoutCap()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Events;
            using Edict.Core.EventHandler;

            namespace Acme.Notifications
            {
                [EdictStream("Orders")]
                public sealed record OrderPlaced : EdictEvent;

                public sealed partial class OrderPlacedEmailHandler : EdictEventHandler
                {
                    System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.Null(entry.SagaTimeoutCap);
    }

    static string SagaSource(string attribute) => $$"""
        using Edict.Contracts.Events;
        using Edict.Contracts.Persistence;
        using Edict.Contracts.Sagas;
        using Edict.Core.Sagas;

        namespace Acme.Shipping
        {
            public sealed record ShipmentProgress : IEdictPersistedState;

            [EdictStream("Orders")]
            public sealed record OrderPlaced : EdictEvent;

            {{attribute}}
            public sealed partial class ShipmentSaga : EdictSaga<ShipmentProgress>
            {
                System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
            }
        }
        """;

    [Fact]
    public void Scan_CommandHandlerRegisteringSchedule_SurfacesInheritsSiloDefaultSource()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Commands;
            using Edict.Contracts.Persistence;
            using Edict.Contracts.Schedules;
            using Edict.Core.Commands;

            namespace Acme.Billing
            {
                public sealed record MeterState : IEdictPersistedState;

                public sealed record StartMeteringCommand : EdictCommand
                {
                    [EdictRouteKey]
                    public System.Guid SubscriptionId { get; init; }
                }

                public sealed record MeterTick : EdictScheduleMessage;

                public sealed partial class MeterCommandHandler : EdictCommandHandler<MeterState>
                {
                    System.Threading.Tasks.Task HandleAsync(StartMeteringCommand command) => System.Threading.Tasks.Task.CompletedTask;
                    System.Threading.Tasks.Task<EdictScheduleResult> HandleAsync(MeterTick tick) => null!;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.Equal(HandlerRole.CommandHandler, entry.Role);
        Assert.NotNull(entry.Schedule);
        Assert.Equal(ScheduleTimeoutSource.InheritsSiloDefault, entry.Schedule!.TimeoutSource);
    }

    [Fact]
    public void Scan_CommandHandlerWithoutSchedule_HasNoScheduleRegistration()
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
                    System.Threading.Tasks.Task HandleAsync(PlaceOrderCommand command) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.Null(entry.Schedule);
    }

    [Fact]
    public void Scan_SagaRegisteringSchedule_SurfacesInheritsSagaCapSource()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Contracts.Schedules;
            using Edict.Core.Sagas;

            namespace Acme.Settlement
            {
                public sealed record SettlementProgress : IEdictPersistedState;

                [EdictStream("Payments")]
                public sealed record PaymentAuthorised : EdictEvent;

                public sealed record PollSettlement : EdictScheduleMessage;

                public sealed partial class SettlementSaga : EdictSaga<SettlementProgress>
                {
                    System.Threading.Tasks.Task HandleAsync(PaymentAuthorised edictEvent) => System.Threading.Tasks.Task.CompletedTask;
                    System.Threading.Tasks.Task<EdictScheduleResult> HandleAsync(PollSettlement poll) => null!;
                }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.Equal(HandlerRole.Saga, entry.Role);
        Assert.NotNull(entry.Schedule);
        Assert.Equal(ScheduleTimeoutSource.InheritsSagaCap, entry.Schedule!.TimeoutSource);
    }

    [Fact]
    public void Scan_CommandValidatorSubclass_ReportsCommandValidatorRoleWithBoundCommand()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Commands;
            using Edict.Core.Commands;

            namespace Acme.Orders
            {
                public sealed record PlaceOrderCommand : EdictCommand
                {
                    [EdictRouteKey]
                    public System.Guid OrderId { get; init; }
                }

                public sealed class PlaceOrderCommandValidator : EdictCommandValidator<PlaceOrderCommand> { }
            }
            """;
        var compilation = CreateCompilation("ConsumerLibrary", EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        // Act
        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var entry = Assert.Single(inventory.Handlers);
        Assert.Equal("Acme.Orders.PlaceOrderCommandValidator", entry.DeclaringTypeName);
        Assert.Equal(HandlerRole.CommandValidator, entry.Role);
        var bound = Assert.Single(entry.BoundContracts);
        Assert.Equal("Acme.Orders.PlaceOrderCommand", bound.FullTypeName);
        Assert.Equal("OrderId", bound.RouteKeyPropertyName);
    }

    [Fact]
    public void Scan_StateProjectionBuilderAndListProjectionBuilder_AreRecognisedAndDistinguishedByRole()
    {
        // Arrange
        const string consumerSource = """
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Projections;

            namespace Acme.Reporting
            {
                public sealed record OrderActivityState : IEdictPersistedState;
                public sealed record OrdersByStatusRow : IEdictPersistedState;

                [EdictStream("Orders")]
                public sealed record OrderPlaced : EdictEvent;

                public sealed partial class OrderActivityProjection : EdictProjectionBuilder<OrderActivityState>
                {
                    System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
                }

                public sealed partial class OrdersByStatusProjection : EdictListProjectionBuilder<OrdersByStatusRow>
                {
                    System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
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
        Assert.Equal(HandlerRole.StateProjectionBuilder, activity.Role);
        Assert.Equal(HandlerRole.ListProjectionBuilder, byStatus.Role);
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
                    System.Threading.Tasks.Task HandleAsync(PlaceOrderCommand command) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;
        const string handlerHalfB = """
            namespace Acme.Orders
            {
                public sealed partial class OrderCommandHandler
                {
                    System.Threading.Tasks.Task HandleAsync(CancelOrderCommand command) => System.Threading.Tasks.Task.CompletedTask;
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

        namespace Edict.Contracts.Tenancy
        {
            [AttributeUsage(AttributeTargets.Struct)]
            public sealed class EdictTenantScopedAttribute : Attribute { }
        }

        namespace Edict.Core.Commands
        {
            using Edict.Contracts.Commands;
            using Edict.Contracts.Persistence;
            public abstract class EdictCommandHandler<TState> where TState : IEdictPersistedState, new() { }
            public abstract class EdictCommandHandler : EdictCommandHandler<EdictUnit> { }
            public sealed class EdictUnit : IEdictPersistedState { }
            public abstract class EdictCommandValidator<TCommand> where TCommand : EdictCommand { }
        }

        namespace Edict.Core.EventHandler
        {
            public abstract class EdictEventHandler { }
        }

        namespace Edict.Contracts.Sagas
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class EdictSagaTimeoutAttribute : Attribute
            {
                public EdictSagaTimeoutAttribute() { }
                public EdictSagaTimeoutAttribute(string duration) { Duration = duration; }
                public string? Duration { get; }
                public bool Unbounded { get; set; }
            }
        }

        namespace Edict.Core.Sagas
        {
            using Edict.Contracts.Persistence;
            public abstract class EdictSaga<TProgress> where TProgress : IEdictPersistedState, new() { }
        }

        namespace Edict.Core.Projections
        {
            using Edict.Contracts.Persistence;
            public abstract class EdictProjectionBuilder<TProjection> where TProjection : IEdictPersistedState, new() { }
            public abstract class EdictListProjectionBuilder<TListProjection> where TListProjection : class, IEdictPersistedState, new() { }
        }

        namespace Edict.Contracts.Schedules
        {
            public abstract record EdictScheduleMessage;
            public abstract record EdictScheduleResult
            {
                public sealed record Continue : EdictScheduleResult;
                public sealed record Complete : EdictScheduleResult;
            }
        }
        """;
}
