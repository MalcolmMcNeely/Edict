using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Idempotency;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using Xunit;

namespace Edict.Architecture.Tests;

// Stands in for a consumer's generated grain interface: the Edict generator
// emits interfaces deriving from the grain-interface markers, and such an
// interface lives in a consumer assembly (not an Edict-owned one), so only the
// marker-assignability clause of the filter can allow-list it. Top-level and
// public so Orleans' own codegen (which runs in this test assembly) can emit a
// valid proxy for it.
public interface IGeneratedHandlerProxy : IEdictCommandHandler;

// Drift guard for the Orleans manifest allow-list (ADR-0070). Orleans 10.2
// refuses any wire/grain-interface type absent from its type-manifest allow-list,
// and Edict's generated grain interfaces and hand-MessagePack contracts bypass
// Orleans' serializer codegen, so AddEdict() registers an ITypeFilter that opts
// them back in. If that registration regresses — or a future Orleans bump or new
// grain species slips outside the predicate — silos stop booting. This asserts
// the invariant through the public AddEdict() seam (no InternalsVisibleTo), so a
// regression fails here rather than in every cluster-booting suite.
public class EdictTypeFilterAllowListTests
{
    public static IEnumerable<object[]> EdictSurfaceTypes() =>
    [
        [typeof(EdictCommand)],
        [typeof(EdictEvent)],
        [typeof(EdictCommandResult)],
        [typeof(IEdictCommandHandler)],
        [typeof(IEdictEventConsumer)],
        [typeof(IGeneratedHandlerProxy)],
        // Framework-internal type that rides persisted grain state; covered by
        // the Edict-owned-assembly clause, the bucket that has no other anchor.
        [typeof(Edict.Core.Sagas.SagaLifecycleState)],
    ];

    [Theory]
    [MemberData(nameof(EdictSurfaceTypes))]
    public void AddEdict_RegistersFilter_AllowingEdictSurface(Type edictType)
    {
        var filters = BuildTypeFilters();

        Assert.True(
            filters.Any(filter => filter.IsTypeAllowed(edictType) == true),
            $"No Edict ITypeFilter allow-lists {edictType.FullName}; Orleans would refuse it at silo manifest build.");
    }

    [Fact]
    public void AddEdict_Filter_DoesNotAllowArbitraryTypes()
    {
        var filters = BuildTypeFilters();

        Assert.All(
            filters,
            filter => Assert.NotEqual(true, filter.IsTypeAllowed(typeof(string))));
    }

    static IReadOnlyList<ITypeFilter> BuildTypeFilters()
    {
        var provider = new ServiceCollection().AddEdict().BuildServiceProvider();
        return provider.GetServices<ITypeFilter>().ToList();
    }
}
