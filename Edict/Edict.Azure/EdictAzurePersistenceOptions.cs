using Azure.Data.Tables;
using Azure.Storage.Blobs;

namespace Edict.Azure;

/// <summary>
/// Tuning knobs for the Azure persistence provider (ADR 0028): the grain-state
/// blob container, the claim-check blob container, the dead-letter table, and
/// the optional service-client overrides. Brand-prefixed because the consumer
/// types it. The claim-check store lives here (not on the streams options)
/// because the bytes physically sit in Azure Blob — same SDK, same
/// connection-string story as grain state.
/// </summary>
public sealed class EdictAzurePersistenceOptions
{
    /// <summary>Container for the Edict grain-state slot (<c>edict-state</c>, ADR 0025).</summary>
    public string GrainStateContainerName { get; set; } = "edict-state";

    /// <summary>Container backing the claim-check escape hatch (ADR 0024).</summary>
    public string ClaimCheckBlobContainerName { get; set; } = "edict-claim-check";

    /// <summary>Azure Table backing the forensic dead-letter projection (ADR 0022).</summary>
    public string DeadLetterTableName { get; set; } = "edict-dead-letter";

    /// <summary>
    /// Optional <see cref="TableServiceClient"/>; a DI-registered singleton
    /// takes precedence so an <c>AddAzureClients()</c>-style power-user setup
    /// works without double-registration.
    /// </summary>
    public TableServiceClient? TableServiceClient { get; set; }

    /// <summary>
    /// Optional <see cref="BlobServiceClient"/>; a DI-registered singleton
    /// takes precedence so an <c>AddAzureClients()</c>-style power-user setup
    /// works without double-registration.
    /// </summary>
    public BlobServiceClient? BlobServiceClient { get; set; }
}
