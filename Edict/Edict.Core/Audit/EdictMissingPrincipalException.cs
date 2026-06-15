namespace Edict.Core.Audit;

/// <summary>
/// Thrown synchronously at an originating <c>SendAsync</c> when auditing is on
/// but the edge resolver yields no principal, before the command is dispatched
/// or anything is persisted. Recording an action attributed to nobody is the
/// compliance breach auditing exists to prevent, so the send is refused at the
/// edge rather than completed silently. A context-free origin (worker, import,
/// admin script, test) supplies a principal explicitly via
/// <c>SendAsync(command, principal)</c> instead.
/// </summary>
public sealed class EdictMissingPrincipalException : Exception
{
    public EdictMissingPrincipalException(Type commandType)
        : base($"Auditing is on but no principal resolved for '{commandType.Name}' at an originating SendAsync. "
            + "The edge resolver registered via AddEdictAudit returned null. Supply a principal explicitly "
            + "with SendAsync(command, EdictPrincipal.Of(...)) for a context-free origin, or fix the resolver.")
    {
    }
}
