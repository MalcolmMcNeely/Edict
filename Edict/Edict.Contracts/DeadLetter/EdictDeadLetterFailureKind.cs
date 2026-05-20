namespace Edict.Contracts.DeadLetter;

/// <summary>
/// Discriminator for the two kinds of dead-letter failure surfaced on the
/// fleet-wide forensic projection (ADR 0022, widened by ADR 0024):
/// <list type="bullet">
///   <item><see cref="EffectFailure"/> — an outbound Outbox effect at the
///         publisher exhausted <c>MaxAttempts</c> (the original
///         dead-letter conceptual surface).</item>
///   <item><see cref="BlobMissing"/> — a receiver could not materialise
///         an inbound event because its claim-check blob had been reaped
///         by the storage account's lifecycle policy (ADR 0024).</item>
/// </list>
/// Existing publisher-side promotions default to <see cref="EffectFailure"/>;
/// the receiver-side promotion path is wired in a later slice.
/// </summary>
public enum EdictDeadLetterFailureKind
{
    EffectFailure = 0,
    BlobMissing = 1,
}
