using Azure.Data.Tables;
using Azure.Storage.Blobs;

namespace Edict.Azure.Persistence;

/// <summary>
/// Tuning knobs for the Azure persistence provider: the grain-state
/// blob container, the dead-letter table, and the optional service-client
/// overrides. Brand-prefixed because the consumer types it. Claim-check
/// settings live on <c>EdictAzureStreamsOptions</c> — claim-check is driven
/// by the queue wire-cap, not grain-state storage.
/// </summary>
public sealed class EdictAzurePersistenceOptions
{
    /// <summary>Container for the Edict grain-state slot (<c>edict-state</c>).</summary>
    public string GrainStateContainerName { get; set; } = "edict-state";

    /// <summary>Azure Table backing the forensic dead-letter projection.</summary>
    public string DeadLetterTableName { get; set; } = "edict-dead-letter";

    /// <summary>
    /// Optional <see cref="TableServiceClient"/>; a DI-registered singleton
    /// takes precedence so an <c>AddAzureClients()</c>-style power-user setup
    /// works without double-registration.
    /// </summary>
    public TableServiceClient? TableServiceClient { get; set; }

    /// <summary>
    /// Optional <see cref="BlobServiceClient"/> for grain-state blobs;
    /// a DI-registered singleton takes precedence so an
    /// <c>AddAzureClients()</c>-style power-user setup works without
    /// double-registration.
    /// </summary>
    public BlobServiceClient? BlobServiceClient { get; set; }
}
