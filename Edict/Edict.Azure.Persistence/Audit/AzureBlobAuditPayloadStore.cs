using Azure;
using Azure.Storage.Blobs;

using Edict.Contracts.Audit;
using Edict.Core.Audit;

namespace Edict.Azure.Persistence.Audit;

// Azure-blob audit payload store: the captured message bodies, one block blob per
// audit record id. Write-once by upload contract (overwrite: false), so a re-drain
// after a crash between the body write and the grain's ack is a no-op and a later
// put can never rewrite a captured body — the conventional analogue of the Postgres
// WORM trigger. Container-level immutability sealing (legal hold / time-based
// retention) is the deferred blob-sealing slice; until it lands the tamper-evidence
// guarantee on Azure is the chain hash, not infra prevention. Get throws
// EdictAuditPayloadNotFoundException for an id the store does not hold.
sealed class AzureBlobAuditPayloadStore : IEdictAuditPayloadStore
{
    readonly BlobContainerClient _container;

    AzureBlobAuditPayloadStore(BlobContainerClient container)
    {
        _container = container;
    }

    // Provisions the backing container. Run once on the host thread at silo wiring —
    // a lazy factory that calls GetAwaiter().GetResult() on first resolve deadlocks
    // on the grain task scheduler when the audit drain reaches for the store.
    public static Task ProvisionAsync(
        BlobServiceClient blobServiceClient,
        string containerName,
        CancellationToken cancellationToken = default) =>
        blobServiceClient.GetBlobContainerClient(containerName).CreateIfNotExistsAsync(cancellationToken: cancellationToken);

    // Wraps an already-provisioned container; no I/O, safe to call from the DI
    // factory.
    public static AzureBlobAuditPayloadStore Create(BlobServiceClient blobServiceClient, string containerName) =>
        new(blobServiceClient.GetBlobContainerClient(containerName));

    // Provision-and-wrap, for fresh dev/test environments that have no AppHost to
    // pre-provision the container against the storage account.
    public static async Task<AzureBlobAuditPayloadStore> CreateAsync(
        BlobServiceClient blobServiceClient,
        string containerName,
        CancellationToken cancellationToken = default)
    {
        await ProvisionAsync(blobServiceClient, containerName, cancellationToken);
        return Create(blobServiceClient, containerName);
    }

    public async Task PutAsync(IReadOnlyList<EdictAuditPayload> payloads, CancellationToken cancellationToken)
    {
        foreach (var payload in payloads)
        {
            var blob = _container.GetBlobClient(payload.RecordId.ToString("N"));
            try
            {
                await blob.UploadAsync(BinaryData.FromBytes(payload.Body), overwrite: false, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException exception) when (exception.Status == 409)
            {
                // The body for this record id is already captured; a re-drain must
                // leave the original untouched.
            }
        }
    }

    public async Task<ReadOnlyMemory<byte>> GetAsync(Guid recordId, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(recordId.ToString("N"));
        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToMemory();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new EdictAuditPayloadNotFoundException(
                recordId, $"No audit payload for record '{recordId:N}' in container '{_container.Name}'.");
        }
    }
}
