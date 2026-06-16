namespace Edict.Contracts.Tenancy;

/// <summary>
/// Thrown by <see cref="EdictTenantId.Of"/> when a candidate tenant string carries
/// a character outside the safe-everywhere set. The character set is a security
/// boundary, not a convenience: a tenant id is folded into routed grain and stream
/// keys, so a value smuggling the key delimiter could forge another tenant's key
/// space. Refusing the value at construction stops an unsafe tenant from ever
/// reaching a key.
/// </summary>
public sealed class EdictInvalidTenantIdException : Exception
{
    public EdictInvalidTenantIdException(string value)
        : base($"Tenant id '{value}' contains a character outside the safe set [A-Za-z0-9._-]. "
            + "A tenant id is folded into routed grain and stream keys, so its character set is a security "
            + "boundary: the key delimiter, path separators, storage-reserved characters, whitespace, control "
            + "characters, and non-ASCII characters are rejected. Map your identity provider's tenant claim to a safe value.")
    {
    }
}
