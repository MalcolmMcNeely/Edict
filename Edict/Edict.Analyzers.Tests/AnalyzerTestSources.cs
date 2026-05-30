namespace Edict.Analyzers.Tests;

/// <summary>
/// Shared C# source fragments used as the analyzer-test compilation input.
/// Centralised so a change to the canonical "well-formed handler" shape lifts
/// every test that depends on it without grep-and-replace.
/// </summary>
internal static class AnalyzerTestSources
{
    /// <summary>A minimal well-formed command + grain used by valid-case tests
    /// across EDICT001/002/003/004.</summary>
    public const string ValidBase = """
        using System;
        using System.Threading.Tasks;
        using Edict.Contracts.Commands;
        using Edict.Core.Commands;
        namespace Sample;
        public sealed record PlaceOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }
        public partial class OrderCommandHandler : EdictCommandHandler
        {
            public Task<EdictCommandResult> HandleAsync(PlaceOrder c) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }
        """;
}
