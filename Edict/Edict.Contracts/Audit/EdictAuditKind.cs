namespace Edict.Contracts.Audit;

/// <summary>
/// Which kind of decision an <see cref="EdictAuditRecord"/> captures: a Command
/// decision (the accept/reject outcome, recorded at C1) or an Event occurrence
/// (recorded at E1). Event capture ships in a later slice; the kind is modelled
/// now so the stored shape is stable.
/// </summary>
public enum EdictAuditKind
{
    /// <summary>A Command decision: the handler accepted or rejected the command.</summary>
    Command,

    /// <summary>An Event occurrence: an event the framework published.</summary>
    Event,
}
