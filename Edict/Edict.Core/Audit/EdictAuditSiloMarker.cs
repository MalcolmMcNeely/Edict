namespace Edict.Core.Audit;

// Registered only by the silo-side WithAudit() switch, so the startup validator
// can tell "this silo captures audit records" apart from "this client only
// stamps origins" and require a store on the silo without tripping on a client.
sealed class EdictAuditSiloMarker;
