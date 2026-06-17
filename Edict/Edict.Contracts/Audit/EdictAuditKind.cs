namespace Edict.Contracts.Audit;

/// <summary>
/// Which kind of decision an <see cref="EdictAuditRecord"/> captures: a Command
/// decision (the accept/reject outcome, recorded at C1) or an Event occurrence
/// (recorded at E1, one record per raised event).
/// </summary>
public enum EdictAuditKind
{
    /// <summary>A Command decision: the handler accepted or rejected the command.</summary>
    Command,

    /// <summary>An Event occurrence: an event the framework published.</summary>
    Event,

    /// <summary>A privileged cross-tenant crossing: an authorized call reached into a tenant other than the calling turn's, recorded so no boundary crossing is silent.</summary>
    TenantCrossing,
}
