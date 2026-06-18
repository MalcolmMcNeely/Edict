using MessagePack;

namespace Edict.Contracts.Tenancy;

/// <summary>
/// The isolation key whose data a Command or Event belongs to: the company wall,
/// not the acting user. An opaque, consumer-supplied string carried as a durable
/// field on <c>EdictCommand</c> and <c>EdictEvent</c>, sibling to
/// <c>EdictPrincipal</c>. Unlike a principal, a tenant id is folded into the
/// routed grain and stream key, so its character set is a security boundary rather
/// than a convenience: a value smuggling the key delimiter could forge another
/// tenant's key space. The constructor admits only a strict safe-everywhere set
/// and rejects anything else, so the charset holds on every path — minting via
/// <see cref="Of"/>, direct construction, and reconstruction off the wire, the
/// constructor being the deserialization entry point for this single-member record.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct EdictTenantId
{
    /// <summary>
    /// Validating deserialization entry point: admits only the safe-everywhere
    /// character set and throws <see cref="EdictInvalidTenantIdException"/>
    /// otherwise, so a value reconstructed off the wire can never carry the key
    /// delimiter or a storage-reserved character into a composed key. Consumer
    /// code mints via <see cref="Of"/>.
    /// </summary>
    public EdictTenantId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsSafe(value))
        {
            throw new EdictInvalidTenantIdException(value);
        }
        Value = value;
    }

    /// <summary>The underlying opaque tenant string, guaranteed safe-everywhere because every construction path validates.</summary>
    public string Value { get; init; }

    /// <summary>
    /// Wraps an opaque tenant string after validating it against the
    /// safe-everywhere character set: ASCII letters, digits, and the three
    /// separators <c>.</c> <c>_</c> <c>-</c>. Rejects <see langword="null"/>, the
    /// empty string, and any value carrying a character that is unsafe in a
    /// composed grain or stream key, a claim-check blob path, an Azure Table key,
    /// or a Postgres partition: the key delimiter, the path separator,
    /// storage-reserved characters, whitespace, control characters, and anything
    /// non-ASCII. The consumer maps their identity provider's tenant claim to a
    /// value in this set.
    /// </summary>
    public static EdictTenantId Of(string value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value;

    static bool IsSafe(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }
        foreach (var character in value)
        {
            var safe = character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
                or '.' or '_' or '-';
            if (!safe)
            {
                return false;
            }
        }
        return true;
    }
}
