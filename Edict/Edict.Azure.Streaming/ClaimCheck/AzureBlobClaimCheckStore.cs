using Azure;
using Azure.Storage.Blobs;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.DeadLetter;

namespace Edict.Azure.Streaming.ClaimCheck;

sealed class AzureBlobClaimCheckStore : IEdictClaimCheckStore
{
    readonly BlobContainerClient _container;

    AzureBlobClaimCheckStore(BlobContainerClient container)
    {
        _container = container;
    }

    // Idempotent safety net for fresh dev/test environments — the AppHost is
    // expected to have provisioned the container against the storage account.
    public static async Task<AzureBlobClaimCheckStore> CreateAsync(
        BlobServiceClient blobServiceClient,
        string containerName,
        CancellationToken cancellationToken = default)
    {
        var container = blobServiceClient.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        return new AzureBlobClaimCheckStore(container);
    }

    public async Task PutAsync(EdictTenantId? tenant, Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(EdictKeyComposer.Compose(tenant, eventId.ToString("N")));
        await blob.UploadAsync(BinaryData.FromBytes(payload), overwrite: false, cancellationToken: cancellationToken);
    }

    public async Task<ReadOnlyMemory<byte>> GetAsync(EdictTenantId? tenant, Guid eventId, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(EdictKeyComposer.Compose(tenant, eventId.ToString("N")));
        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToMemory();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new EdictClaimCheckFetchException(
                eventId,
                $"Claim-check payload not found for event '{eventId:N}'.");
        }
    }
}
