using Edict.Contracts.Audit;
using Edict.Core.Audit;
using Edict.Core.Serialization;

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
}
