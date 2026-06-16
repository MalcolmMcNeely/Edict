using Azure.Data.Tables;

using Edict.Azure.Persistence.Audit;
using Edict.Contracts.Audit;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.Outbox;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Audit;

// Seam test for the Azure-table audit chain store against real Azurite. The store
// fans each record out to one row per access path (entity, correlation, principal)
// and rebuilds order at query time: ByEntity follows the per-aggregate sequence,
// ByCorrelation and ByPrincipal rebuild cross-grain order by intent-time. Arbitrary
// principal strings that are illegal in an Azure key still round-trip, and a
// re-append is a no-op.
public sealed class AzureTableAuditStoreTests
{
    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(serializer => serializer
            .AddAssembly(typeof(CounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer());
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    static async Task<AzureTableAuditStore> CreateStoreAsync()
    {
        var connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        var tableServiceClient = new TableServiceClient(connectionString);
        var tableName = $"edictauditrecord{Guid.NewGuid():N}";
        return await AzureTableAuditStore.CreateAsync(tableServiceClient, BuildSerializer(), tableName);
    }

    static EdictAuditRecord Record(
        string entityType,
        string entityKey,
        long sequence,
        Guid correlationId,
        DateTimeOffset occurredAt,
        EdictPrincipal? principal,
        EdictAuditKind kind = EdictAuditKind.Command) =>
        new()
        {
            RecordId = Guid.NewGuid(),
            Kind = kind,
            CorrelationId = correlationId,
            EntityType = entityType,
            EntityKey = entityKey,
            MessageType = "Msg",
            OccurredAt = occurredAt,
            Sequence = sequence,
            Principal = principal,
            PreviousHash = [1, 2, 3],
            RecordHash = [4, 5, 6],
            PayloadHash = [7, 8, 9],
        };

    [Fact]
    public async Task ByEntity_ReturnsTheAggregatesRecordsOrderedBySequence()
    {
        // Arrange — append out of sequence order under one aggregate.
        var store = await CreateStoreAsync();
        var entityKey = Guid.NewGuid().ToString();
        var baseTime = DateTimeOffset.UtcNow;
        var principal = EdictPrincipal.Of("auditor-bob");
        var records = new[]
        {
            Record("Order", entityKey, 2, Guid.NewGuid(), baseTime.AddSeconds(2), principal),
            Record("Order", entityKey, 0, Guid.NewGuid(), baseTime, principal),
            Record("Order", entityKey, 1, Guid.NewGuid(), baseTime.AddSeconds(1), principal),
        };
        await store.AppendAsync(records, CancellationToken.None);

        // Act
        var read = await store.ByEntityAsync("Order", entityKey, CancellationToken.None);

        // Assert
        Assert.Equal([0L, 1L, 2L], read.Select(record => record.Sequence));
    }

    [Fact]
    public async Task ByCorrelation_ReturnsEveryRecordOnTheCorrelation_OrderedByIntentTime()
    {
        // Arrange — one correlation spanning two aggregates.
        var store = await CreateStoreAsync();
        var correlationId = Guid.NewGuid();
        var baseTime = DateTimeOffset.UtcNow;
        var principal = EdictPrincipal.Of("auditor-bob");
        await store.AppendAsync(
            [
                Record("Stock", "s1", 0, correlationId, baseTime.AddSeconds(2), principal),
                Record("Order", "o1", 0, correlationId, baseTime, principal),
                Record("Order", "o1", 1, correlationId, baseTime.AddSeconds(1), principal),
            ],
            CancellationToken.None);

        // Act
        var read = await store.ByCorrelationAsync(correlationId, CancellationToken.None);

        // Assert — every record, ordered by intent-time.
        Assert.Equal(3, read.Count);
        Assert.All(read, record => Assert.Equal(correlationId, record.CorrelationId));
        var occurredTimes = read.Select(record => record.OccurredAt).ToList();
        Assert.Equal(occurredTimes.OrderBy(time => time).ToList(), occurredTimes);
    }

    [Fact]
    public async Task ByPrincipal_RoundTripsAKeyHostilePrincipal_AndHonoursTheWindow()
    {
        // Arrange — a principal whose value carries characters illegal in an Azure
        // key (slash, backslash, hash, question mark).
        var store = await CreateStoreAsync();
        var principal = EdictPrincipal.Of("realm/tenant\\user#42?x");
        var correlationId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;
        var inWindow = Record("Order", "o1", 0, correlationId, before.AddSeconds(1), principal);
        await store.AppendAsync([inWindow], CancellationToken.None);
        var after = before.AddSeconds(5);

        // Act
        var withinWindow = await store.ByPrincipalAsync(principal, before, after, CancellationToken.None);
        var afterWindow = await store.ByPrincipalAsync(principal, after, after.AddMinutes(1), CancellationToken.None);

        // Assert — the escaped principal round-trips, and the lower bound is honoured.
        var single = Assert.Single(withinWindow);
        Assert.Equal(principal, single.Principal);
        Assert.Empty(afterWindow);
    }

    [Fact]
    public async Task ByEntity_InRange_FiltersByIntentTime()
    {
        // Arrange
        var store = await CreateStoreAsync();
        var entityKey = Guid.NewGuid().ToString();
        var principal = EdictPrincipal.Of("auditor-bob");
        var before = DateTimeOffset.UtcNow;
        await store.AppendAsync(
            [
                Record("Order", entityKey, 0, Guid.NewGuid(), before.AddSeconds(1), principal),
                Record("Order", entityKey, 1, Guid.NewGuid(), before.AddMinutes(10), principal),
            ],
            CancellationToken.None);

        // Act — a window that holds only the first record.
        var read = await store.ByEntityAsync("Order", entityKey, before, before.AddMinutes(1), CancellationToken.None);

        // Assert
        Assert.Equal([0L], read.Select(record => record.Sequence));
    }

    [Fact]
    public async Task Append_IsIdempotent_SoARedrainDoesNotDoubleCount()
    {
        // Arrange
        var store = await CreateStoreAsync();
        var entityKey = Guid.NewGuid().ToString();
        var principal = EdictPrincipal.Of("auditor-bob");
        var record = Record("Order", entityKey, 0, Guid.NewGuid(), DateTimeOffset.UtcNow, principal);

        // Act — drain the same record twice.
        await store.AppendAsync([record], CancellationToken.None);
        await store.AppendAsync([record], CancellationToken.None);

        // Assert
        var read = await store.ByEntityAsync("Order", entityKey, CancellationToken.None);
        Assert.Single(read);
    }

    [Fact]
    public async Task ByPrincipal_DoesNotReturnARecordCapturedWithoutAPrincipal()
    {
        // Arrange — an unattributed record (null principal) writes no principal row.
        var store = await CreateStoreAsync();
        var entityKey = Guid.NewGuid().ToString();
        var before = DateTimeOffset.UtcNow;
        await store.AppendAsync(
            [Record("Order", entityKey, 0, Guid.NewGuid(), before.AddSeconds(1), principal: null)],
            CancellationToken.None);

        // Act + Assert — present on the entity path, absent from any principal path.
        var byEntity = await store.ByEntityAsync("Order", entityKey, CancellationToken.None);
        Assert.Single(byEntity);
        var byPrincipal = await store.ByPrincipalAsync(EdictPrincipal.Of("auditor-bob"), before, before.AddMinutes(1), CancellationToken.None);
        Assert.Empty(byPrincipal);
    }
}
