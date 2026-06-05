namespace Edict.Analyzers.Tests;

/// <summary>
/// Shared C# source fragments used as the analyzer-test compilation input.
/// Centralised so a change to the canonical "well-formed handler" shape lifts
/// every test that depends on it without grep-and-replace.
/// </summary>
internal static class AnalyzerTestSources
{
    /// <summary>A minimal well-formed command + grain used by valid-case tests
    /// across EDICT001/002/003/004. Derives from the canonical generic
    /// <c>EdictCommandHandler&lt;TState&gt;</c> shape that every Sample handler uses,
    /// so a generics-naive analyzer fails its own valid-case test instead of
    /// silently passing against the non-generic shim.</summary>
    public const string ValidBase = """
        using System;
        using System.Threading.Tasks;
        using Edict.Contracts.Commands;
        using Edict.Contracts.Persistence;
        using Edict.Core.Commands;
        namespace Sample;
        public sealed record PlaceOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }
        public sealed class OrderState : IEdictPersistedState;
        public partial class OrderCommandHandler : EdictCommandHandler<OrderState>
        {
            Task<EdictCommandResult> HandleAsync(PlaceOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }
        """;
}
