using System.Text;

using Azure.Storage.Blobs;

using Edict.Azure.Persistence.Audit;
using Edict.Contracts.Audit;
using Edict.Core.Audit;
using Edict.Tests.Conformance;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Audit;

// Seam test for the Azure-blob audit payload store against real Azurite: a body
// put under a record id round-trips, a re-put of the same id is a no-op (write-once
// dedup so a re-drain cannot rewrite the body), and a missing id surfaces the typed
// not-found exception.
public sealed class AzureBlobAuditPayloadStoreTests
{
    static async Task<AzureBlobAuditPayloadStore> CreateStoreAsync()
    {
        var connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        var blobServiceClient = new BlobServiceClient(connectionString);
        var containerName = $"edict-audit-payload-{Guid.NewGuid():N}";
        return await AzureBlobAuditPayloadStore.CreateAsync(blobServiceClient, containerName);
    }

    [Fact]
    public async Task PutThenGet_ReturnsTheStoredBody()
    {
        // Arrange
        var store = await CreateStoreAsync();
        var recordId = Guid.NewGuid();
        var body = Encoding.UTF8.GetBytes("captured-body");

        // Act
        await store.PutAsync([new EdictAuditPayload(recordId, body)], CancellationToken.None);
        var fetched = await store.GetAsync(recordId, CancellationToken.None);

        // Assert
        Assert.Equal(body, fetched.ToArray());
    }

    [Fact]
    public async Task Put_IsIdempotent_SoARedrainCannotRewriteTheBody()
    {
        // Arrange
        var store = await CreateStoreAsync();
        var recordId = Guid.NewGuid();
        var original = Encoding.UTF8.GetBytes("original-body");
        var tampered = Encoding.UTF8.GetBytes("tampered-body");

        // Act — the second put under the same record id must be a no-op.
        await store.PutAsync([new EdictAuditPayload(recordId, original)], CancellationToken.None);
        await store.PutAsync([new EdictAuditPayload(recordId, tampered)], CancellationToken.None);

        // Assert
        var fetched = await store.GetAsync(recordId, CancellationToken.None);
        Assert.Equal(original, fetched.ToArray());
    }

    [Fact]
    public async Task Get_ForAnUnknownRecord_ThrowsAuditPayloadNotFound()
    {
        // Arrange
        var store = await CreateStoreAsync();
        var unknownRecordId = Guid.NewGuid();

        // Act + Assert
        var exception = await Assert.ThrowsAsync<EdictAuditPayloadNotFoundException>(
            () => store.GetAsync(unknownRecordId, CancellationToken.None));
        Assert.Equal(unknownRecordId, exception.RecordId);
    }
}
