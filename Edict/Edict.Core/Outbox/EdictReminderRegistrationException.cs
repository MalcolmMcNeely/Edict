using Orleans;

namespace Edict.Core.Outbox;

/// <summary>
/// Thrown when registering or unregistering a durability-backstop reminder fails for the
/// whole cold-start retry budget. Every backstop — outbox drain, schedule reactivation,
/// saga absolute-lifetime cap, audit drain — arms through the one reminder registrar, which
/// absorbs the transient "reminder service is still initializing" window with a bounded
/// retry. This surfaces only when that window never closes within the budget, which Orleans
/// itself flags as a silo-wide reminder outage needing a restart — so the registrar fails
/// loud rather than leave a backstop silently unarmed.
/// </summary>
/// <remarks>
/// On the command paths the registration runs inside the called grain, so this exception is
/// serialized back across the grain→caller hop. It carries an Orleans codec so the failure
/// reaches the caller as itself, with its message intact, rather than as an opaque
/// <c>CodecNotFoundException</c> that hides why the call failed — the same trap fixed for
/// <c>EdictCrossTenantAccessException</c>.
/// </remarks>
[GenerateSerializer]
public sealed class EdictReminderRegistrationException : Exception
{
    public EdictReminderRegistrationException(string operation, Exception innerException)
        : base($"Registering a reminder ('{operation}') failed after exhausting the cold-start retry budget. "
            + "The Orleans reminder service was still initializing for the whole budget, which Orleans flags as "
            + "a silo-wide outage needing a restart.", innerException)
    {
    }
}
