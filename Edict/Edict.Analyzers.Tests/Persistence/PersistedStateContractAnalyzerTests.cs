using Edict.Analyzers.Persistence;

using Xunit;

namespace Edict.Analyzers.Tests.Persistence;

public class PersistedStateContractAnalyzerTests
{
    [Fact]
    public void EDICT011_ShouldNotRaise_WhenPersistedStateHasGenerateSerializerLiteralAliasAndIdOnEveryProperty()
    {
        const string source = """
            using Edict.Contracts.Persistence;
            using Orleans;
            namespace Sample;
            [GenerateSerializer]
            [Alias("Sample.OrderState")]
            public sealed class OrderState : IEdictPersistedState
            {
                [Id(0)]
                public int Count { get; set; }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new PersistedStateContractAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT011_ShouldRaiseMissingGenerateSerializer_WhenPersistedStateLacksTheAttribute()
    {
        const string source = """
            using Edict.Contracts.Persistence;
            using Orleans;
            namespace Sample;
            [Alias("Sample.OrderState")]
            public sealed class OrderState : IEdictPersistedState
            {
                [Id(0)]
                public int Count { get; set; }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new PersistedStateContractAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT011", d.Id);
        Assert.Contains("GenerateSerializer", d.GetMessage());
    }

    [Fact]
    public void EDICT011_ShouldRaiseMissingAlias_WhenPersistedStateLacksTheAttribute()
    {
        const string source = """
            using Edict.Contracts.Persistence;
            using Orleans;
            namespace Sample;
            [GenerateSerializer]
            public sealed class OrderState : IEdictPersistedState
            {
                [Id(0)]
                public int Count { get; set; }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new PersistedStateContractAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT011", d.Id);
        Assert.Contains("[Alias", d.GetMessage());
    }

    [Fact]
    public void EDICT011_ShouldRaiseAliasNotStringLiteral_WhenAliasArgumentIsNameof()
    {
        const string source = """
            using Edict.Contracts.Persistence;
            using Orleans;
            namespace Sample;
            [GenerateSerializer]
            [Alias(nameof(OrderState))]
            public sealed class OrderState : IEdictPersistedState
            {
                [Id(0)]
                public int Count { get; set; }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new PersistedStateContractAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT011", d.Id);
        Assert.Contains("nameof", d.GetMessage());
    }

    [Fact]
    public void EDICT011_ShouldRaisePropertyMissingId_WhenDeclaredPublicPropertyLacksIdAttribute()
    {
        const string source = """
            using Edict.Contracts.Persistence;
            using Orleans;
            namespace Sample;
            [GenerateSerializer]
            [Alias("Sample.OrderState")]
            public sealed class OrderState : IEdictPersistedState
            {
                public int Count { get; set; }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new PersistedStateContractAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT011", d.Id);
        Assert.Contains("Count", d.GetMessage());
        Assert.Contains("[Id(n)]", d.GetMessage());
    }

    [Fact]
    public void EDICT011_ShouldNotRaisePropertyMissingId_WhenPropertyIsInheritedFromBaseClass()
    {
        // The check covers *declared* public instance properties only; a
        // base class owns its own [Id(n)] discipline.
        const string source = """
            using Edict.Contracts.Persistence;
            using Orleans;
            namespace Sample;
            public abstract class StateBase
            {
                public int Inherited { get; set; }
            }
            [GenerateSerializer]
            [Alias("Sample.OrderState")]
            public sealed class OrderState : StateBase, IEdictPersistedState
            {
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new PersistedStateContractAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT011_ShouldNotRaise_WhenTypeDoesNotImplementPersistedState()
    {
        const string source = """
            namespace Sample;
            public sealed class JustAPoco
            {
                public int Count { get; set; }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new PersistedStateContractAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT011_ShouldRaiseEveryViolation_WhenPersistedStateMissesAllAttributes()
    {
        // One diagnostic per missing sub-descriptor so the codefix's batched
        // Quick Action satisfies them all in one edit.
        const string source = """
            using Edict.Contracts.Persistence;
            namespace Sample;
            public sealed class OrderState : IEdictPersistedState
            {
                public int Count { get; set; }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new PersistedStateContractAnalyzer());

        Assert.Equal(3, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("EDICT011", d.Id));
    }
}
