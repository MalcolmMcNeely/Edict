using System.ComponentModel;

using Edict.Contracts.Tenancy;

namespace Edict.Contracts.Routing;

/// <summary>
/// The one chokepoint every generator-emitted route-key site folds through to
/// build a grain or stream key: the command grain key, the event stream key, and
/// the projection/saga grain key that rides the stream all compose here rather
/// than each reinventing the fold. Lives in the contracts surface so the
/// stream-accessor registrar can be emitted into a contracts-only assembly.
/// <para>
/// Public so generated code can reference it; hidden from consumer IntelliSense
/// because no consumer composes a key by hand.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EdictKeyComposer
{
    const char Delimiter = '|';

    /// <summary>
    /// Folds a tenant and a stringified route key into the routed key. A public
    /// aggregate composes to the bare route key; a tenant-scoped aggregate composes
    /// to <c>"{tenant}|{guid}"</c>. The delimiter cannot appear in a tenant id (the
    /// <see cref="EdictTenantId.Of"/> charset excludes it) nor in the <c>"N"</c>-format
    /// route Guid, so the composed key carries exactly one delimiter and
    /// <see cref="Parse"/> recovers the pair unambiguously.
    /// </summary>
    public static string Compose(EdictTenantId? tenant, string routeKey) =>
        tenant is { } wall ? $"{wall.Value}{Delimiter}{routeKey}" : routeKey;

    /// <summary>
    /// Recovers the tenant and route Guid from a composed key, the inverse of
    /// <see cref="Compose"/>. A bare key parses to no tenant; a <c>"{tenant}|{guid}"</c>
    /// key parses to the exact tenant and route Guid. Throws
    /// <see cref="EdictMalformedRoutedKeyException"/> rather than mis-parse anything
    /// that is not a key this composer could have produced: a non-Guid route part, an
    /// empty side, a tenant outside the safe-everywhere charset, or more than one
    /// delimiter. This is what lets the isolation call filter compare the tenant
    /// parsed from a grain's own key against the ambient tenant, and forensics answer
    /// which tenant owns a row from the key alone.
    /// </summary>
    public static EdictRoutedKey Parse(string composedKey)
    {
        ArgumentNullException.ThrowIfNull(composedKey);

        var delimiterIndex = composedKey.IndexOf(Delimiter);
        if (delimiterIndex < 0)
        {
            return new EdictRoutedKey(null, ParseRouteGuid(composedKey, composedKey));
        }

        var tenantPart = composedKey[..delimiterIndex];
        var routePart = composedKey[(delimiterIndex + 1)..];

        // A second delimiter means the key was never one this composer produced; the
        // route Guid carries none and a tenant id cannot, so refuse to guess which
        // side the boundary falls on.
        if (routePart.Contains(Delimiter))
        {
            throw new EdictMalformedRoutedKeyException(composedKey);
        }

        EdictTenantId tenant;
        try
        {
            tenant = EdictTenantId.Of(tenantPart);
        }
        catch (EdictInvalidTenantIdException)
        {
            throw new EdictMalformedRoutedKeyException(composedKey);
        }

        return new EdictRoutedKey(tenant, ParseRouteGuid(composedKey, routePart));
    }

    static Guid ParseRouteGuid(string composedKey, string routePart) =>
        Guid.TryParse(routePart, out var routeGuid)
            ? routeGuid
            : throw new EdictMalformedRoutedKeyException(composedKey);
}
