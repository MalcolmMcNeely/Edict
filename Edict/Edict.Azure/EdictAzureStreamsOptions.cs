using Azure.Storage.Queues;

namespace Edict.Azure;

/// <summary>
/// Tuning knobs for the Azure streams provider (ADR 0028). Lives in
/// <c>Edict.Azure</c> because the wire-cap that constrains
/// <see cref="ClaimCheckThresholdBytes"/> is the Azure Queue / Azure Table
/// per-property limit; a future Kafka provider would expose its own
/// equivalent. Brand-prefixed because the consumer types it.
/// </summary>
public sealed class EdictAzureStreamsOptions
{
    /// <summary>Orleans stream-provider name. Edict's runtime is hardcoded to <c>"edict"</c>.</summary>
    public string StreamProviderName { get; set; } = "edict";

    /// <summary>
    /// Inner-event byte length above which the commit pipeline uploads to
    /// the claim-check blob store instead of riding the body inline (ADR
    /// 0024). Default 30 720 — 2 KB of headroom against the 32 KB
    /// per-property cap to absorb envelope framing.
    /// </summary>
    public int ClaimCheckThresholdBytes { get; set; } = 30_720;

    /// <summary>Azure Queue pulling-agent poll period. Faster is more responsive but more chatty.</summary>
    public TimeSpan QueuePollingPeriod { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Optional <see cref="QueueServiceClient"/>; a DI-registered singleton
    /// takes precedence so an <c>AddAzureClients()</c>-style power-user setup
    /// works without double-registration.
    /// </summary>
    public QueueServiceClient? QueueServiceClient { get; set; }
}
