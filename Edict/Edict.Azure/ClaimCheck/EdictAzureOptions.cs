namespace Edict.Azure.ClaimCheck;

/// <summary>
/// Consumer-tunable knobs for the Azure provider (ADR 0024). Lives in
/// <c>Edict.Azure</c> because the constraining byte-cap is set by the
/// publish-path storage stack (Azure Table 32 KB per-property); a future
/// Kafka provider would expose its own equivalent with a much higher
/// default. Brand-prefixed because the consumer types it.
/// </summary>
public sealed class EdictAzureOptions
{
    /// <summary>
    /// Inner-event byte length above which the commit pipeline uploads to
    /// the claim-check blob store instead of riding the body inline (ADR
    /// 0024). Default 30 720 — 2 KB of headroom against the 32 KB
    /// per-property cap to absorb envelope framing.
    /// </summary>
    public int ClaimCheckThresholdBytes { get; set; } = 30_720;

    /// <summary>
    /// Blob container under the storage account that backs the claim-check
    /// escape hatch. Provisioned by <c>Sample.AppHost</c> against Azurite
    /// for the sample app; in production the operator's bicep / terraform
    /// owns it.
    /// </summary>
    public string ClaimCheckContainerName { get; set; } = "edict-claim-check";
}
