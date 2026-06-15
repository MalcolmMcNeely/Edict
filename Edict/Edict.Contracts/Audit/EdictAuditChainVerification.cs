namespace Edict.Contracts.Audit;

/// <summary>
/// The result of verifying one aggregate's audit chain. Either every link
/// recomputes and chains, or verification names the first broken record so an
/// auditor knows exactly where the history was altered.
/// </summary>
/// <param name="IsIntact">True when every record's hash recomputes and links to its predecessor.</param>
/// <param name="BrokenAtSequence">The <see cref="EdictAuditRecord.Sequence"/> of the first record whose hash or linkage failed; <see langword="null"/> when intact.</param>
public sealed record EdictAuditChainVerification(bool IsIntact, long? BrokenAtSequence)
{
    /// <summary>An unbroken chain.</summary>
    public static EdictAuditChainVerification Intact() => new(true, null);

    /// <summary>A chain broken at the record with the given sequence.</summary>
    public static EdictAuditChainVerification BrokenAt(long sequence) => new(false, sequence);
}
