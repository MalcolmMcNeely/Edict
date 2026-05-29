using Azure.Storage.Queues;

namespace Edict.Azure.Streaming;

/// <summary>
/// Tuning knobs for the Azure streams provider. Lives in
/// <c>Edict.Azure.Streaming</c> because the wire-cap that constrains
/// <see cref="ClaimCheckThresholdBytes"/> is the Azure Queue per-property
/// limit; a future Kafka provider would expose its own equivalent.
/// Brand-prefixed because the consumer types it. Claim-check store
/// location is configured separately via <c>AddEdictAzureBlobClaimCheck</c>
/// (Azure-blob substrate) or by registering an <c>IEdictClaimCheckStore</c>
/// the persistence side ships (e.g. <c>AddEdictPostgresPersistence</c>) —
/// the wire-cap concern (this options class) is orthogonal to where the
/// offloaded bytes physically sit.
/// </summary>
public sealed class EdictAzureStreamsOptions
{
    /// <summary>Orleans stream-provider name. Edict's runtime is hardcoded to <c>"edict"</c>.</summary>
    public string StreamProviderName { get; set; } = "edict";

    /// <summary>
    /// Inner-event byte length above which the commit pipeline uploads to
    /// the claim-check blob store instead of riding the body inline.
    /// Default 30 720 — 2 KB of headroom against the 32 KB
    /// per-property cap to absorb envelope framing.
    /// </summary>
    public int ClaimCheckThresholdBytes { get; set; } = 30_720;

    /// <summary>
    /// Azure Queue pulling-agent poll period. This is a hard floor on
    /// per-event latency — the consumer cannot observe an event until the next
    /// poll tick after the publisher's queue PUT. The Orleans default is
    /// 100 ms; Edict ships 10 ms so interactive workloads aren't pinned to
    /// the floor out of the box. Lower is more responsive but more chatty
    /// (each tick costs a queue GET per consumer queue, billed and
    /// rate-limited under real Azure Storage). Raise it for cost-sensitive
    /// workloads where end-to-end latency tolerates seconds.
    /// </summary>
    public TimeSpan QueuePollingPeriod { get; set; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Number of Azure queues the stream provider fans out across. Orleans'
    /// default is 8; Edict ships 16 so consumer parallelism is not capped by an
    /// Orleans-conservative default. Each queue is polled independently at
    /// <see cref="QueuePollingPeriod"/>, so the choice is a direct cost /
    /// parallelism trade-off on real Azure Storage: at the 10 ms default poll
    /// period the per-queue GET cost runs roughly $3–6/day per silo per 8
    /// queues. Raise it (32, 64) for higher consumer throughput on workloads
    /// that pay back the storage bill; drop it (4, 8) for cost-sensitive
    /// workloads where the throughput floor is fine.
    /// </summary>
    public int NumQueues { get; set; } = 16;

    /// <summary>
    /// Optional <see cref="QueueServiceClient"/>; a DI-registered singleton
    /// takes precedence so an <c>AddAzureClients()</c>-style power-user setup
    /// works without double-registration.
    /// </summary>
    public QueueServiceClient? QueueServiceClient { get; set; }
}
