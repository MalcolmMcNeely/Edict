using Azure.Storage.Blobs;

using Edict.Azure.Streaming.ClaimCheck;
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
    public async Task PutAsync_ShouldStoreBodyThatRoundTripsViaGetAsync()
    {
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, _containerName);
        var eventId = Guid.NewGuid();
        byte[] payload = [0x10, 0x20, 0x30, 0x40, 0x50];

        await store.PutAsync(eventId, payload, CancellationToken.None);
        var fetched = await store.GetAsync(eventId, CancellationToken.None);

        Assert.Equal(payload, fetched.ToArray());
    }

    [Fact]
    public async Task GetAsync_ShouldThrowFetchException_WhenBodyMissing()
    {
        // A never-written EventId is the same logical failure the Postgres store
        // raises — surfacing the shared typed exception lets both substrates
        // classify into the same Substrate bucket instead of leaking the Azure
        // SDK's concrete RequestFailedException.
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, _containerName);
        var eventId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<EdictClaimCheckFetchException>(
            () => store.GetAsync(eventId, CancellationToken.None));
        Assert.Equal(eventId, exception.EventId);
    }

    [Fact]
    public async Task PutAsync_ShouldThrow_WhenSameEventIdWrittenTwice()
    {
        // overwrite:false makes a duplicate write a loud collision-detector — a
        // breach of the assign-once EventId-uniqueness invariant surfaces as a
        // fault rather than silently overwriting the parked body.
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, _containerName);
        var eventId = Guid.NewGuid();
        await store.PutAsync(eventId, new byte[] { 1 }, CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(
            () => store.PutAsync(eventId, new byte[] { 2 }, CancellationToken.None));
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
