using System.Text;

using Azure.Data.Tables;
using Azure.Storage.Blobs;

using Edict.Azure.Persistence;
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

// Pins the client-side wiring a non-silo Azure process (the Sample web host) uses to
// read the audit log: registering the Azure Table chain store and Azure Blob payload
// store against pre-built service clients and resolving an IEdictAuditRepository over
// them. AzureTableAuditStore.Create / AzureBlobAuditPayloadStore.Create do no I/O, so
// the thin registration resolves without a live storage account; the round-trip test
// writes through the silo-side stores and reads back through the reader-registered
// repository against real Azurite, the way the Sample web reads what the silo captured.
public sealed class AddEdictAzureAuditReaderTests
{
    [Fact]
    public void AddEdictAzureAuditReader_ShouldResolveAnAuditRepository()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSerializer();

        // Act
        services.AddEdictAzureAuditReader(options =>
        {
            options.TableServiceClient = new TableServiceClient("UseDevelopmentStorage=true");
            options.BlobServiceClient = new BlobServiceClient("UseDevelopmentStorage=true");
        });

        // Assert
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IEdictAuditRepository>());
    }

    [Fact]
    public async Task AddEdictAzureAuditReader_ReadsBackARecordAndPayloadCapturedThroughTheStores()
    {
        // Arrange — write through the silo-side stores against Azurite.
        var connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        var tableServiceClient = new TableServiceClient(connectionString);
        var blobServiceClient = new BlobServiceClient(connectionString);
        var auditTableName = $"edictauditrecord{Guid.NewGuid():N}";
        var auditPayloadContainerName = $"edict-audit-payload-{Guid.NewGuid():N}";

        var serializer = BuildSerializer();
        var chainStore = await AzureTableAuditStore.CreateAsync(tableServiceClient, serializer, auditTableName);
        var payloadStore = await AzureBlobAuditPayloadStore.CreateAsync(blobServiceClient, auditPayloadContainerName);

        var entityKey = Guid.NewGuid().ToString();
        var record = new EdictAuditRecord
        {
            RecordId = Guid.NewGuid(),
            Kind = EdictAuditKind.Command,
            CorrelationId = Guid.NewGuid(),
            EntityType = "Order",
            EntityKey = entityKey,
            MessageType = "PlaceOrder",
            OccurredAt = DateTimeOffset.UtcNow,
            Sequence = 0,
            Principal = EdictPrincipal.Of("auditor-bob"),
            PreviousHash = [],
            RecordHash = [4, 5, 6],
            PayloadHash = [7, 8, 9],
        };
        var body = Encoding.UTF8.GetBytes("captured-body");
        await chainStore.AppendAsync([record], CancellationToken.None);
        await payloadStore.PutAsync([new EdictAuditPayload(record.RecordId, body)], CancellationToken.None);

        var services = new ServiceCollection();
        services.AddSerializer(serializer => serializer
            .AddAssembly(typeof(CounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer());

        // Act — read back only through the reader-registered repository.
        services.AddEdictAzureAuditReader(options =>
        {
            options.TableServiceClient = tableServiceClient;
            options.BlobServiceClient = blobServiceClient;
            options.AuditTableName = auditTableName;
            options.AuditPayloadContainerName = auditPayloadContainerName;
        });
        using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IEdictAuditRepository>();

        var chain = await repository.ByEntityAsync("Order", entityKey);
        var readPayload = await repository.GetPayloadAsync(record.RecordId);

        // Assert
        var read = Assert.Single(chain);
        Assert.Equal(record.RecordId, read.RecordId);
        Assert.Equal(EdictPrincipal.Of("auditor-bob"), read.Principal);
        Assert.Equal(body, readPayload.ToArray());
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(serializer => serializer
            .AddAssembly(typeof(CounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer());
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }
}
