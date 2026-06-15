namespace Edict.Core.Audit;

// Presence in the container is the origin-stamping on-switch. Registered by both
// WithAudit() (the silo switch the startup validator pairs against the resolver)
// and AddEdictAudit() (so an Orleans client that only registers the resolver
// still stamps at its own origin sends).
sealed class EdictAuditEnabledMarker;
