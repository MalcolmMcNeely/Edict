using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;
using Edict.Core.Tenancy;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Persistence-axis conformance for the tenant-scoped audit read: the tenant is a
/// queryable column and the scoping is pushed into the store, so an ambient-scoped
/// read returns only the caller's wall (another wall's rows never leave the store),
/// the operator superset still sees every wall, and a read with no ambient tenant
/// fails closed. Records are seeded store-direct because the conformance capture silo
/// runs tenancy-off and captures only public records.
/// </summary>
public abstract class AuditTenantScopeScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IAuditConformanceFixture
{
    static readonly EdictTenantId Acme = EdictTenantId.Of("acme");
    static readonly EdictTenantId Globex = EdictTenantId.Of("globex");
    static readonly DateTimeOffset Window = new(2026, 6, 18, 0, 0, 0, TimeSpan.Zero);

    readonly TFixture _fixture;

    protected AuditTenantScopeScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TenantFilter_NarrowsToOneWallInTheStore_WhileTheOperatorSupersetSeesEveryWall()
    {
        // Arrange — one correlation that touched two walls and a public aggregate.
        var correlationId = Guid.NewGuid();
        await SeedThreeWallsAsync(correlationId);

        // Act + Assert — a non-null filter narrows to one wall: the predicate ran in
        // the store, so neither the other tenant nor the public record came back.
        var acmeOnly = await _fixture.OperatorByCorrelationAsync(correlationId, Acme);
        Assert.Equal(Acme, Assert.Single(acmeOnly).Tenant);

        // The operator superset passes no filter and sees every wall, public among them.
        var everyWall = await _fixture.OperatorByCorrelationAsync(correlationId, tenant: null);
        Assert.Equal(3, everyWall.Count);
        Assert.Contains(everyWall, record => record.Tenant == Acme);
        Assert.Contains(everyWall, record => record.Tenant == Globex);
        Assert.Single(everyWall, record => record.Tenant is null);
    }

    [Fact]
    public async Task AmbientScopedRead_ReturnsOnlyTheCallersWall()
    {
        // Arrange — the same three walls under one correlation.
        var correlationId = Guid.NewGuid();
        await SeedThreeWallsAsync(correlationId);

        // Act — the ambient resolver resolves the acme wall.
        var scoped = await _fixture.TenantScopedByCorrelationAsync(correlationId, Acme);

        // Assert — only the caller's own record, with the predicate pushed into the store.
        Assert.Equal(Acme, Assert.Single(scoped).Tenant);
    }

    [Fact]
    public async Task AmbientScopedRead_FailsClosed_WhenNoAmbientTenant()
    {
        await Assert.ThrowsAsync<EdictMissingTenantException>(
            () => _fixture.TenantScopedByCorrelationAsync(Guid.NewGuid(), ambientTenant: null));
    }

    Task SeedThreeWallsAsync(Guid correlationId) =>
        _fixture.AppendAsync(
        [
            RecordFor(Acme, "acme|order-1", correlationId, sequence: 0),
            RecordFor(Globex, "globex|order-1", correlationId, sequence: 1),
            RecordFor(tenant: null, "settlement-1", correlationId, sequence: 2),
        ]);

    static EdictAuditRecord RecordFor(EdictTenantId? tenant, string entityKey, Guid correlationId, long sequence) => new()
    {
        RecordId = Guid.NewGuid(),
        Kind = EdictAuditKind.Command,
        Tenant = tenant,
        Principal = EdictPrincipal.Of("auditor"),
        ConversationId = correlationId,
        EntityType = "OrderCommandHandler",
        EntityKey = entityKey,
        OccurredAt = Window.AddSeconds(sequence),
        Sequence = sequence,
    };
}
