using Azure.Storage.Blobs;

using Edict.Azure.Streaming.ClaimCheck;
using Edict.Contracts.ClaimCheck;
using Edict.Core.DeadLetter;
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
    public async Task GetAsync_ShouldThrowPayloadMissing_WhenBlobMissing()
    {
        // A well-formed key whose blob is absent is the same logical failure the
        // Postgres store raises as PayloadMissing — surfacing the shared typed
        // exception lets both substrates classify into the same Substrate bucket
        // instead of leaking the Azure SDK's concrete RequestFailedException.
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, _containerName);

        var exception = await Assert.ThrowsAsync<EdictClaimCheckFetchException>(
            () => store.GetAsync("missing-blob-key", CancellationToken.None));
        Assert.Equal(EdictClaimCheckFetchException.Reason.PayloadMissing, exception.FetchReason);
        Assert.Equal("missing-blob-key", exception.Key);
    }

    [Fact]
    public async Task GetAsync_ShouldThrowKeyMalformed_WhenKeyIsBlank()
    {
        // Parity with the Postgres store's pre-fetch guard: a key the store
        // cannot even attempt a lookup with surfaces as KeyMalformed, which the
        // classifier routes to Serialization rather than Substrate.
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, _containerName);

        var exception = await Assert.ThrowsAsync<EdictClaimCheckFetchException>(
            () => store.GetAsync("   ", CancellationToken.None));
        Assert.Equal(EdictClaimCheckFetchException.Reason.KeyMalformed, exception.FetchReason);
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
