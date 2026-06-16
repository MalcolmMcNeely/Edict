using Edict.Contracts.Tenancy;

namespace Edict.Core.Tests.Tenancy;

// The value the tenant fixture's edge resolver returns. A test sets it to drive
// both the happy path (a tenant present) and fail-closed (null), since silo and
// client share one process and therefore one static.
static class TenantSource
{
    public static EdictTenantId? Current { get; set; } = EdictTenantId.Of("acme");
}
