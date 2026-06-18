using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;
using Edict.Core.Audit;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using Xunit;

namespace Edict.Core.Tests.Audit;

// The operator-scoped IEdictAuditRepository is the fleet superset: it returns every
// record across every tenant, and because the tenant is a first-class field on the
// record, the Public (tenant-less) versus Tenant distinction is visible per-record
// without a separate read. This is the operator counterpart to the ambient-scoped
// reader, which would see only one wall.
public sealed class AuditOperatorSupersetTests
{
    static readonly Guid Correlation = new("33333333-3333-3333-3333-333333333333");
    static readonly DateTimeOffset Window = new(2026, 6, 17, 0, 0, 0, TimeSpan.Zero);

    static EdictAuditRecord RecordFor(EdictTenantId? tenant, string entityKey, long sequence) => new()
    {
        RecordId = Guid.NewGuid(),
        Kind = EdictAuditKind.Command,
        Tenant = tenant,
        CorrelationId = Correlation,
        EntityType = "OrderCommandHandler",
        EntityKey = entityKey,
        OccurredAt = Window.AddSeconds(sequence),
        Sequence = sequence,
    };

    [Fact]
    public async Task ByCorrelationAsync_ShouldReturnAllTenants_WithThePublicTenantDistinctionPerRecord()
    {
        // Arrange — one correlation that touched two tenants and a public aggregate.
        var store = new RecordingAuditStore();
        await store.AppendAsync(
        [
            RecordFor(EdictTenantId.Of("acme"), "acme|order-1", 0),
            RecordFor(EdictTenantId.Of("globex"), "globex|order-1", 1),
            RecordFor(tenant: null, "settlement-1", 2),
        ],
            CancellationToken.None);
        var repository = OperatorRepositoryOver(store);

        // Act — the operator read returns the whole superset, unfiltered.
        var records = await repository.ByCorrelationAsync(Correlation);

        // Assert — every tenant is present, and the Public record is the one whose
        // tenant is null, so an operator distinguishes Public from Tenant on the record.
        Assert.Equal(3, records.Count);
        Assert.Contains(records, record => record.Tenant == EdictTenantId.Of("acme"));
        Assert.Contains(records, record => record.Tenant == EdictTenantId.Of("globex"));
        Assert.Single(records, record => record.Tenant is null);
    }

    [Fact]
    public async Task ByCorrelationAsync_ShouldReturnOnlyTheGivenWall_WhenFilteredByTenant()
    {
        // Arrange — the same correlation across two tenants and a public aggregate.
        var store = new RecordingAuditStore();
        await store.AppendAsync(
        [
            RecordFor(EdictTenantId.Of("acme"), "acme|order-1", 0),
            RecordFor(EdictTenantId.Of("globex"), "globex|order-1", 1),
            RecordFor(tenant: null, "settlement-1", 2),
        ],
            CancellationToken.None);
        var repository = OperatorRepositoryOver(store);

        // Act — an operator narrows the same query to one wall via the tenant filter.
        var records = await repository.ByCorrelationAsync(Correlation, EdictTenantId.Of("acme"));

        // Assert — only the acme wall's record comes back; the predicate, not the
        // caller, dropped the other tenant and the public record.
        Assert.Equal(EdictTenantId.Of("acme"), Assert.Single(records).Tenant);
    }

    static IEdictAuditRepository OperatorRepositoryOver(IEdictAuditStore store)
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddEdictContractSerializer());
        services.AddSingleton(store);
        services.AddSingleton<IEdictAuditPayloadStore>(new RecordingAuditPayloadStore());
        var provider = services.BuildServiceProvider();
        return new EdictDefaultAuditRepository(store, provider.GetRequiredService<IEdictAuditPayloadStore>(), provider.GetRequiredService<Serializer>());
    }
}
