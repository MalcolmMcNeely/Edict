using Edict.Contracts.Audit;

namespace Edict.Core.Audit;

// The edge seam that yields the principal for the current originating context.
// Registered by AddEdictAudit; consulted once per origin send. Kept a plain Func
// behind this interface so Edict.Contracts and Edict.Core never reference an IdP
// SDK or ClaimsPrincipal.
interface IEdictPrincipalResolver
{
    EdictPrincipal? Resolve();
}
