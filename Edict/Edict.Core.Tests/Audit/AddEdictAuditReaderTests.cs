using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;
using Edict.Core.Audit;
using Edict.Core.Serialization;
using Edict.Core.Tenancy;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using Xunit;

namespace Edict.Core.Tests.Audit;

// Pins the client-side read registration: a process that is not a silo (the
// Sample web host) registers the audit stores it can reach and AddEdictAuditReader
// hands it an IEdictAuditRepository over them, so a page can query the chain
// without the silo-only WithAudit() switch.
public sealed class AddEdictAuditReaderTests
{
    [Fact]
    public async Task AddEdictAuditReader_ShouldResolveARepository_DelegatingToTheRegisteredStores()
    {
        // Arrange
        var store = new RecordingAuditStore();
        await store.AppendAsync(
            [new EdictAuditRecord { RecordId = Guid.NewGuid(), EntityType = "OrderCommandHandler", EntityKey = "order-1", Sequence = 0 }],
            CancellationToken.None);

        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddEdictContractSerializer());
        services.AddSingleton<IEdictAuditStore>(store);
        services.AddSingleton<IEdictAuditPayloadStore>(new RecordingAuditPayloadStore());

        // Act
        services.AddEdictAuditReader();

        // Assert
        await using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IEdictAuditRepository>();

        var records = await repository.ByEntityAsync("OrderCommandHandler", "order-1");
        var record = Assert.Single(records);
        Assert.Equal("order-1", record.EntityKey);
    }

    [Fact]
    public async Task AddEdictAuditReader_ShouldResolveTheTenantScopedReader_ScopedToTheAmbientTenant()
    {
        // Arrange — two records on the same entity under different tenants.
        var store = new RecordingAuditStore();
        await store.AppendAsync(
        [
            new EdictAuditRecord { RecordId = Guid.NewGuid(), EntityType = "OrderCommandHandler", EntityKey = "acme|order-1", Sequence = 0, Tenant = EdictTenantId.Of("acme") },
            new EdictAuditRecord { RecordId = Guid.NewGuid(), EntityType = "OrderCommandHandler", EntityKey = "acme|order-1", Sequence = 1, Tenant = EdictTenantId.Of("globex") },
        ],
            CancellationToken.None);

        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddEdictContractSerializer());
        services.AddSingleton<IEdictAuditStore>(store);
        services.AddSingleton<IEdictAuditPayloadStore>(new RecordingAuditPayloadStore());
        services.AddSingleton<IEdictTenantResolver>(new FixedTenantResolver(EdictTenantId.Of("acme")));

        // Act
        services.AddEdictAuditReader();

        // Assert — the tenant-scoped reader hands back only the ambient tenant's record.
        await using var provider = services.BuildServiceProvider();
        var reader = provider.GetRequiredService<IEdictTenantScopedAuditRepository>();

        var records = await reader.ByEntityAsync("OrderCommandHandler", "acme|order-1");
        Assert.Equal(EdictTenantId.Of("acme"), Assert.Single(records).Tenant);
    }

    sealed class FixedTenantResolver(EdictTenantId tenant) : IEdictTenantResolver
    {
        public EdictTenantId? Resolve() => tenant;
    }
}
