using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;
using Edict.Core.Audit;
using Edict.Core.Tenancy;

using Xunit;

namespace Edict.Core.Tests.Audit;

// The ambient-scoped audit read returns only the caller's own tenant trail: where
// the operator-scoped IEdictAuditRepository is the fleet superset, this filters
// every query to the resolved ambient tenant, so a tenant sees its own records and
// neither another tenant's nor the public (tenant-less) ones. A read with no
// ambient tenant fails closed rather than surfacing another wall's trail.
public sealed class EdictTenantScopedAuditRepositoryTests
{
    static readonly DateTimeOffset Window = new(2026, 6, 17, 0, 0, 0, TimeSpan.Zero);

    static EdictAuditRecord RecordFor(EdictTenantId? tenant, string entityKey) => new()
    {
        RecordId = Guid.NewGuid(),
        Kind = EdictAuditKind.Command,
        Tenant = tenant,
        Principal = EdictPrincipal.Of("alice"),
        CorrelationId = new Guid("22222222-2222-2222-2222-222222222222"),
        EntityType = "OrderCommandHandler",
        EntityKey = entityKey,
        OccurredAt = Window,
    };

    [Fact]
    public async Task ByCorrelationAsync_ShouldReturnOnlyTheAmbientTenantRecords()
    {
        var inner = new StubAuditRepository(
        [
            RecordFor(EdictTenantId.Of("acme"), "acme|order-1"),
            RecordFor(EdictTenantId.Of("globex"), "globex|order-2"),
            RecordFor(tenant: null, "order-public"),
        ]);
        var repository = ReaderFor(inner, EdictTenantId.Of("acme"));

        var records = await repository.ByCorrelationAsync(new Guid("22222222-2222-2222-2222-222222222222"));

        Assert.All(records, record => Assert.Equal(EdictTenantId.Of("acme"), record.Tenant));
        Assert.Single(records);
    }

    [Fact]
    public async Task ByPrincipalAsync_ShouldReturnOnlyTheAmbientTenantRecords()
    {
        var inner = new StubAuditRepository(
        [
            RecordFor(EdictTenantId.Of("acme"), "acme|order-1"),
            RecordFor(EdictTenantId.Of("globex"), "globex|order-2"),
        ]);
        var repository = ReaderFor(inner, EdictTenantId.Of("acme"));

        var records = await repository.ByPrincipalAsync(EdictPrincipal.Of("alice"), Window, Window.AddDays(1));

        Assert.Equal(EdictTenantId.Of("acme"), Assert.Single(records).Tenant);
    }

    [Fact]
    public async Task ByEntityAsync_ShouldReturnOnlyTheAmbientTenantRecords()
    {
        var inner = new StubAuditRepository(
        [
            RecordFor(EdictTenantId.Of("acme"), "acme|order-1"),
            RecordFor(EdictTenantId.Of("globex"), "globex|order-1"),
        ]);
        var repository = ReaderFor(inner, EdictTenantId.Of("acme"));

        var records = await repository.ByEntityAsync("OrderCommandHandler", "acme|order-1");

        Assert.Equal(EdictTenantId.Of("acme"), Assert.Single(records).Tenant);
    }

    [Fact]
    public async Task ByEntityAsync_ShouldPushTheAmbientTenantIntoTheStore_RatherThanFilterInMemory()
    {
        var inner = new StubAuditRepository([RecordFor(EdictTenantId.Of("acme"), "acme|order-1")]);
        var repository = ReaderFor(inner, EdictTenantId.Of("acme"));

        await repository.ByEntityAsync("OrderCommandHandler", "acme|order-1");

        // The resolved wall reached the operator query as its filter, so the predicate
        // runs in the store and another tenant's rows never come back to be re-dropped.
        Assert.Equal(EdictTenantId.Of("acme"), inner.LastTenantFilter);
    }

    [Fact]
    public async Task ByCorrelationAsync_ShouldFailClosed_WhenNoAmbientTenant()
    {
        var repository = ReaderFor(new StubAuditRepository([]), tenant: null);

        await Assert.ThrowsAsync<EdictMissingTenantException>(
            () => repository.ByCorrelationAsync(Guid.NewGuid()));
    }

    static EdictTenantScopedAuditRepository ReaderFor(IEdictAuditRepository inner, EdictTenantId? tenant) =>
        new(inner, new StubTenantResolver(tenant));

    sealed class StubTenantResolver(EdictTenantId? tenant) : IEdictTenantResolver
    {
        public EdictTenantId? Resolve() => tenant;
    }
}
