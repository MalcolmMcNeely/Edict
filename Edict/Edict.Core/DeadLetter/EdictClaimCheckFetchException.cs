namespace Edict.Core.DeadLetter;

/// <summary>
/// Runtime fault raised when a receiver-side claim-check lookup cannot
/// produce a payload. <see cref="FetchReason"/> discriminates the two
/// observable failure modes: a malformed key the store can't even attempt
/// (Serialization bucket) versus a well-formed key whose blob is absent
/// (Substrate bucket — typically a TTL'd or never-written row).
/// </summary>
public sealed class EdictClaimCheckFetchException : Exception
{
    public EdictClaimCheckFetchException(Reason reason, string key, string message)
        : base(message)
    {
        FetchReason = reason;
        Key = key;
    }

    public Reason FetchReason { get; }

    public string Key { get; }

    public enum Reason
    {
        KeyMalformed,
        PayloadMissing,
    }
}
