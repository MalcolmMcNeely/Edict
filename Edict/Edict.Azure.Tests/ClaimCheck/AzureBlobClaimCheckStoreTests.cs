using Azure;
using Azure.Storage.Blobs;

using Edict.Azure.Streaming.ClaimCheck;
using Edict.Contracts.ClaimCheck;
using Edict.Tests.Conformance;

namespace Edict.Azure.Tests.ClaimCheck;

public sealed class AzureBlobClaimCheckStoreTests : IAsyncLifetime
{
    BlobServiceClient _blobServiceClient = null!;
    string _containerName = "";

    public async Task InitializeAsync()
    {
        var connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _blobServiceClient = new BlobServiceClient(connectionString);
        _containerName = $"edict-claim-check-{Guid.NewGuid():N}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PutAsync_ShouldReturnKeyThatRoundTripsViaGetAsync()
    {
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, _containerName);
        byte[] payload = [0x10, 0x20, 0x30, 0x40, 0x50];

        var key = await store.PutAsync(payload, CancellationToken.None);
        var fetched = await store.GetAsync(key, CancellationToken.None);

        Assert.Equal(payload, fetched.ToArray());
    }

    [Fact]
    public async Task GetAsync_ShouldThrowRequestFailed_WhenBlobMissing()
    {
        // The receiver-side dead-letter promotion path keys on RequestFailed
        // with status 404 to fold a missing blob into the IDeadLetterPromoter
        // pipeline — this asserts the exception shape that path recognises.
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, _containerName);

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => store.GetAsync("missing-blob-key", CancellationToken.None));
        Assert.Equal(404, exception.Status);
    }

    [Fact]
    public async Task PutAsync_ShouldGenerateUniqueKeysAcrossCalls()
    {
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, _containerName);

        var k1 = await store.PutAsync(new byte[] { 1 }, CancellationToken.None);
        var k2 = await store.PutAsync(new byte[] { 2 }, CancellationToken.None);

        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void AzureBlobClaimCheckStore_ShouldNotExposeDeleteApi()
    {
        // Append-only invariant: the seam forbids DeleteAsync; this is a
        // structural guard that the Azure provider doesn't add one through a
        // side door.
        var method = typeof(AzureBlobClaimCheckStore).GetMethod("DeleteAsync");
        Assert.Null(method);
    }
}
