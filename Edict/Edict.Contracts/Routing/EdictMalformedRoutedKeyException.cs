namespace Edict.Contracts.Routing;

/// <summary>
/// Thrown by <see cref="EdictKeyComposer.Parse"/> when a key is not one
/// <see cref="EdictKeyComposer.Compose"/> could have produced: a non-Guid route
/// part, an empty side, a tenant outside the safe-everywhere charset, or more than
/// one delimiter. Parse refuses to guess where the boundary falls rather than
/// mis-attribute a row to the wrong tenant, so a key it cannot decompose
/// unambiguously fails loud. A framework-composed key is always well-formed, so this
/// surfacing marks a genuine keying fault, never normal traffic.
/// </summary>
public sealed class EdictMalformedRoutedKeyException : Exception
{
    public EdictMalformedRoutedKeyException(string composedKey)
        : base($"Routed key '{composedKey}' is not a key EdictKeyComposer could have composed: "
            + "expected a bare \"N\"-format route Guid, or \"{tenant}|{guid}\" with a safe-charset tenant and one delimiter.")
    {
    }
}
