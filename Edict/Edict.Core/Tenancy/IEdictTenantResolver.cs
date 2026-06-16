using Edict.Contracts.Tenancy;

namespace Edict.Core.Tenancy;

// The edge seam that yields the tenant for the current originating context.
// Registered by AddEdictTenant; consulted once per origin send. Kept a plain Func
// behind this interface so Edict.Contracts and Edict.Core never reference an IdP
// SDK or ClaimsPrincipal, the same shape as IEdictPrincipalResolver.
interface IEdictTenantResolver
{
    EdictTenantId? Resolve();
}
