using Edict.Contracts.Tenancy;

namespace Edict.Core.Tenancy;

// Binds the consumer's resolver delegate to the root service provider so the
// delegate can read an ambient identity seam (an IHttpContextAccessor, a signed
// tenant claim) at resolve time. The provider flows from AsyncLocal-backed
// accessors, so the singleton root is the correct one to hand over.
sealed class DelegateTenantResolver(IServiceProvider serviceProvider, Func<IServiceProvider, EdictTenantId?> resolve)
    : IEdictTenantResolver
{
    public EdictTenantId? Resolve() => resolve(serviceProvider);
}
