using Azure;
using Azure.Storage.Blobs;

using Edict.Azure.ClaimCheck;
using Edict.Contracts.ClaimCheck;

namespace Edict.Azure.Tests.ClaimCheck;

/// <summary>
/// Azurite-backed conformance for <see cref="AzureBlobClaimCheckStore"/>
/// (ADR 0024). Uses the assembly-shared Azurite (ADR 0029) and a per-class
/// Guid-prefixed container so it never collides with other tests against the
/// same Azurite. The test does not need a <see cref="TestCluster"/> — it
/// exercises the blob store directly.
/// </summary>
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
        // The receiver-side dead-letter promotion path (slice 3) keys on
        // RequestFailed with status 404 to fold a missing blob into the
        // existing IDeadLetterPromoter pipeline — assert the exception type
        // is what that path will recognise.
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, _containerName);

        var ex = await Assert.ThrowsAsync<RequestFailedException>(
            () => store.GetAsync("missing-blob-key", CancellationToken.None));
        Assert.Equal(404, ex.Status);
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
        // Append-only invariant (Model B, ADR 0024). The seam already forbids
        // DeleteAsync; this guard is a belt-and-braces structural check that
        // the Azure provider does not add one through a side door.
        var method = typeof(AzureBlobClaimCheckStore).GetMethod("DeleteAsync");
        Assert.Null(method);
    }
}
