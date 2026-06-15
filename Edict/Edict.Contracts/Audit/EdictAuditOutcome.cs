namespace Edict.Contracts.Audit;

/// <summary>
/// How a Command decision resolved, recorded on an <see cref="EdictAuditRecord"/>
/// of kind <see cref="EdictAuditKind.Command"/>. A rejection is the answer to the
/// regulator's first question ("why was this denied, and who tried?"), which a
/// stream-based scheme could never capture.
/// </summary>
public enum EdictAuditOutcome
{
    /// <summary>The command was accepted and handled.</summary>
    Accepted,

    /// <summary>The command was rejected; the reasons are on the record.</summary>
    Rejected,
}
