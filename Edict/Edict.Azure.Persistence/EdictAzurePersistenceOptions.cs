using Azure.Data.Tables;
using Azure.Storage.Blobs;

namespace Edict.Azure.Persistence;

/// <summary>
/// Tuning knobs for the Azure persistence provider: the grain-state
/// blob container and the optional service-client overrides. Brand-prefixed
/// because the consumer types it. Claim-check settings live on
/// <c>EdictAzureStreamsOptions</c> — claim-check is driven by the queue
/// wire-cap, not grain-state storage.
/// </summary>
public sealed class EdictAzurePersistenceOptions
{
    /// <summary>Container for the Edict grain-state slot (<c>edict-state</c>).</summary>
    public string GrainStateContainerName { get; set; } = "edict-state";

    /// <summary>
    /// Azure Table holding the audit chain (one fan-out append row per access
    /// path). The chain is tamper-<em>evidence</em> via its hash chain, but the
    /// Azure-Table backing has no infra tamper-<em>prevention</em> (no WORM trigger,
    /// as Postgres has) until the deferred blob-sealing slice — a privileged
    /// operator can alter a row, and <c>VerifyEntityChainAsync</c> is then what
    /// surfaces the break.
    /// </summary>
    public string AuditTableName { get; set; } = "edictauditrecord";

    /// <summary>
    /// Container for the audit payload bodies, one write-once blob per audit record
    /// id. Container-level immutability sealing is the deferred blob-sealing slice;
    /// until then the body is append-only by upload contract, not by infra policy.
    /// </summary>
    public string AuditPayloadContainerName { get; set; } = "edict-audit-payload";

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
