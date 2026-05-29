using Azure.Storage.Blobs;

namespace Edict.Azure.Streaming.ClaimCheck;

/// <summary>
/// Tuning knobs for the Azure-blob-backed claim-check store. Wiring this
/// store is the consumer's choice: an AQS consumer pairing with Azure
/// persistence calls <c>AddEdictAzureBlobClaimCheck</c>; an AQS consumer
/// pairing with Postgres persistence skips it and lets
/// <c>AddEdictPostgresPersistence</c> register the Postgres-backed store.
/// Brand-prefixed because the consumer types it.
/// </summary>
public sealed class EdictAzureBlobClaimCheckOptions
{
    /// <summary>Container backing the claim-check escape hatch.</summary>
    public string ContainerName { get; set; } = "edict-claim-check";

    /// <summary>
    /// Optional <see cref="BlobServiceClient"/>; a DI-registered singleton
    /// takes precedence so an <c>AddAzureClients()</c>-style power-user setup
    /// works without double-registration.
    /// </summary>
    public BlobServiceClient? BlobServiceClient { get; set; }
}
