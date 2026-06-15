using MessagePack;

namespace Edict.Contracts.Audit;

/// <summary>
/// The actor on whose authority a Command was issued or an Event raised: ALCOA's
/// <em>Attributable</em>. An opaque, consumer-supplied string (an <c>oid</c>, a
/// <c>sub</c>, an employee id, a service-principal name, a realm slug) carried as
/// a durable field on <c>EdictCommand</c> and <c>EdictEvent</c>. Edict imposes no
/// format validation: what a valid principal looks like is the consumer's
/// identity-provider domain. Minted by <see cref="Of"/> so a value is always
/// deliberate; the public constructor exists only for the serializer.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct EdictPrincipal
{
    /// <summary>Deserialization entry point; consumer code mints via <see cref="Of"/>.</summary>
    public EdictPrincipal(string value) => Value = value;

    /// <summary>The underlying opaque actor string.</summary>
    public string Value { get; init; }

    /// <summary>
    /// Wraps an opaque actor string. Rejects only <see langword="null"/> (a
    /// principal with no value is meaningless); any non-null string is accepted
    /// unchanged, because format is the identity provider's concern, not Edict's.
    /// </summary>
    public static EdictPrincipal Of(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new EdictPrincipal(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
