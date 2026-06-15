using Edict.Contracts.Audit;

namespace Edict.Core.Audit;

// Binds the consumer's resolver delegate to the root service provider so the
// delegate can read an ambient identity seam (an IHttpContextAccessor, say) at
// resolve time. The provider flows from AsyncLocal-backed accessors, so the
// singleton root is the correct one to hand over.
sealed class DelegatePrincipalResolver(IServiceProvider serviceProvider, Func<IServiceProvider, EdictPrincipal?> resolve)
    : IEdictPrincipalResolver
{
    public EdictPrincipal? Resolve() => resolve(serviceProvider);
}
