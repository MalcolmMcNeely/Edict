using Edict.Mcp.Handlers;

namespace Edict.AgenticTooling.Architecture.Tests;

static class HandlerRoleFixtures
{
    public static string ConsumerSourceFor(HandlerRole role) => role switch
    {
        HandlerRole.CommandHandler => """
            using Edict.Contracts.Commands;
            using Edict.Contracts.Persistence;
            using Edict.Core.Commands;

            namespace Acme.Fixture
            {
                public sealed record OrderState : IEdictPersistedState;
                public sealed record PlaceOrderCommand : EdictCommand;
                public sealed partial class OrderCommandHandler : EdictCommandHandler<OrderState>
                {
                    System.Threading.Tasks.Task HandleAsync(PlaceOrderCommand command) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """,
        HandlerRole.CommandValidator => """
            using Edict.Contracts.Commands;
            using Edict.Core.Commands;

            namespace Acme.Fixture
            {
                public sealed record PlaceOrderCommand : EdictCommand;
                public sealed class PlaceOrderCommandValidator : EdictCommandValidator<PlaceOrderCommand> { }
            }
            """,
        HandlerRole.EventHandler => """
            using Edict.Contracts.Events;
            using Edict.Core.EventHandler;

            namespace Acme.Fixture
            {
                [EdictStream("Orders")]
                public sealed record OrderPlaced : EdictEvent;
                public sealed partial class OrderPlacedEmailHandler : EdictEventHandler
                {
                    System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """,
        HandlerRole.Saga => """
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Sagas;

            namespace Acme.Fixture
            {
                public sealed record ShipmentProgress : IEdictPersistedState;
                [EdictStream("Orders")]
                public sealed record OrderPlaced : EdictEvent;
                public sealed partial class ShipmentSaga : EdictSaga<ShipmentProgress>
                {
                    System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """,
        HandlerRole.StateProjectionBuilder => """
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Projections;

            namespace Acme.Fixture
            {
                public sealed record OrderActivityState : IEdictPersistedState;
                [EdictStream("Orders")]
                public sealed record OrderPlaced : EdictEvent;
                public sealed partial class OrderActivityProjection : EdictProjectionBuilder<OrderActivityState>
                {
                    System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """,
        HandlerRole.ListProjectionBuilder => """
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Projections;

            namespace Acme.Fixture
            {
                public sealed record OrdersByStatusRow : IEdictPersistedState;
                [EdictStream("Orders")]
                public sealed record OrderPlaced : EdictEvent;
                public sealed partial class OrdersByStatusProjection : EdictListProjectionBuilder<OrdersByStatusRow>
                {
                    System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """,
        _ => throw new InvalidOperationException($"No fixture defined for HandlerRole.{role}."),
    };

    public const string EdictBasesSource = """
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
        """;
}
