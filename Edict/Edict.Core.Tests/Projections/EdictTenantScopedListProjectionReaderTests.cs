using Edict.Contracts.Tenancy;
using Edict.Core.Projections;
using Edict.Core.Tenancy;

using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Runtime;

using Xunit;

namespace Edict.Core.Tests.Projections;

// The read path scopes by the ambient tenant exactly as the write path composes it.
// The consumer passes no partition, so the only partition reachable is the one the
// framework folds the ambient tenant into — a raw partition key cannot be expressed.
// A read with no ambient tenant fails closed rather than scoping to a default partition.
public sealed class EdictTenantScopedListProjectionReaderTests
{
    sealed class DirectoryRow;

    [Fact]
    public async Task QueryMyPartitionAsync_ShouldAddressTheAmbientTenantPartition()
    {
        var factory = new RecordingGrainFactory();
        var reader = ReaderFor(factory, EdictTenantId.Of("acme"));

        await reader.QueryMyPartitionAsync();

        Assert.Equal("acme|00000000000000000000000000000000", factory.LastGrainKey);
    }

    [Fact]
    public async Task GetMyAsync_ShouldAddressTheRowWithinTheAmbientTenantPartition()
    {
        var factory = new RecordingGrainFactory();
        var reader = ReaderFor(factory, EdictTenantId.Of("globex"));

        await reader.GetMyAsync("9b2c0f5d4e1a4b8c9d3e2f1a0b5c6d7e");

        Assert.Equal("globex|9b2c0f5d4e1a4b8c9d3e2f1a0b5c6d7e", factory.LastGrainKey);
    }

    [Fact]
    public async Task QueryMyPartitionAsync_ShouldFailClosed_WhenNoAmbientTenant()
    {
        var reader = ReaderFor(new RecordingGrainFactory(), tenant: null);

        await Assert.ThrowsAsync<EdictMissingTenantException>(() => reader.QueryMyPartitionAsync());
    }

    static EdictTenantScopedListProjectionReader<DirectoryRow> ReaderFor(IGrainFactory grainFactory, EdictTenantId? tenant)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ProjectionReadRouteResolver(
            new Dictionary<Type, string> { [typeof(DirectoryRow)] = "DirectoryProjection" }));
        services.AddSingleton(grainFactory);
        services.AddSingleton<IEdictTenantResolver>(new StubTenantResolver(tenant));
        return new EdictTenantScopedListProjectionReader<DirectoryRow>(services.BuildServiceProvider());
    }

    sealed class StubTenantResolver(EdictTenantId? tenant) : IEdictTenantResolver
    {
        public EdictTenantId? Resolve() => tenant;
    }
}
