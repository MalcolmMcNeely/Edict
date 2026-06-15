using Edict.Contracts.Audit;

namespace Edict.Core.Tests.Audit;

// The value the audit fixture's edge resolver returns. A test sets it to drive
// both the happy path (a principal present) and fail-closed (null), since silo
// and client share one process and therefore one static.
static class AuditPrincipalSource
{
    public static EdictPrincipal? Current { get; set; } = EdictPrincipal.Of("alice");
}
