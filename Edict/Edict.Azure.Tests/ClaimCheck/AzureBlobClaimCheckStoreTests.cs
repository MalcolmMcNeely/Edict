using Azure;
using Azure.Storage.Blobs;

using Edict.Azure.ClaimCheck;
using Edict.Contracts.ClaimCheck;

using Testcontainers.Azurite;

namespace Edict.Azure.Tests.ClaimCheck;

/// <summary>
/// Azurite-backed conformance for <see cref="AzureBlobClaimCheckStore"/>
/// (ADR 0024, AC: PutAsync round-trips via GetAsync; missing-blob GetAsync
/// throws a recognisable exception; the framework never deletes blobs). The
/// test owns its own Azurite container so it can prove the
/// store-creates-container behaviour without coupling to the larger
/// <c>AzureClusterFixture</c>.
/// </summary>
public sealed class AzureBlobClaimCheckStoreTests : IAsyncLifetime
{
    AzuriteContainer _azurite = null!;
    BlobServiceClient _blobServiceClient = null!;
    const string ContainerName = "edict-claim-check-tests";

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCreateParameterModifier(p =>
            {
                p.Cmd ??= [];
                p.Cmd.Add("--skipApiVersionCheck");
            })
            .Build();
        await _azurite.StartAsync();
        _blobServiceClient = new BlobServiceClient(_azurite.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_azurite is not null)
        {
            await _azurite.DisposeAsync();
        }
    }

    [Fact]
    public async Task PutAsync_ShouldReturnKeyThatRoundTripsViaGetAsync()
    {
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, ContainerName);
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
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, ContainerName);

        var ex = await Assert.ThrowsAsync<RequestFailedException>(
            () => store.GetAsync("missing-blob-key", CancellationToken.None));
        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task PutAsync_ShouldGenerateUniqueKeysAcrossCalls()
    {
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, ContainerName);

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
