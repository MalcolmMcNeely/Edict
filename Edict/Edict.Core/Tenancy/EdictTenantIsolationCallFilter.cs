using System.Diagnostics;

using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.Commands;
using Edict.Telemetry;

using Orleans;

namespace Edict.Core.Tenancy;

/// <summary>
/// The runtime backstop for tenant isolation, registered only when tenancy is on. It
/// guards direct command calls — the path a stolen route key would travel — by parsing
/// the tenant from the target grain's own key and comparing it to the calling turn's
/// ambient tenant. The common path is silent: every key is composed from the ambient
/// tenant, so key-tenant equals relay-tenant by construction. A divergence is either a
/// keying bug that bypassed the composition chokepoint or an illegitimate reach into
/// another wall, so the filter throws <see cref="EdictCrossTenantAccessException"/>
/// unless the crossing was explicitly authorized, in which case it proceeds and records
/// the crossing as a span event. Projection grains ride streams, not direct calls, and
/// are isolated structurally by their composed key, so the filter leaves them untouched.
/// </summary>
sealed class EdictTenantIsolationCallFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        // Only direct command calls carry the stolen-key threat; projection and system
        // grains pass through. A public-aggregate key carries no delimiter and no wall
        // to enforce, so it never reaches the parse.
        var grainKey = context.TargetId.Key.ToString();
        if (context.Grain is not IEdictCommandHandler || grainKey is null || !grainKey.Contains('|'))
        {
            await context.Invoke();
            return;
        }

        var keyTenant = EdictKeyComposer.Parse(grainKey).Tenant;
        var relayTenant = TenantRelay.Current();
        if (keyTenant == relayTenant)
        {
            await context.Invoke();
            return;
        }

        if (TenantCrossing.IsAuthorized())
        {
            RecordCrossing(SemanticConventions.Tenant.Events.CrossTenantAuthorized, relayTenant, keyTenant);
            await context.Invoke();
            return;
        }

        RecordCrossing(SemanticConventions.Tenant.Events.CrossTenantDenied, relayTenant, keyTenant);
        throw new EdictCrossTenantAccessException(relayTenant, keyTenant);
    }

    static void RecordCrossing(string eventName, EdictTenantId? relayTenant, EdictTenantId? keyTenant) =>
        Activity.Current?.AddEvent(new ActivityEvent(eventName, tags: new ActivityTagsCollection
        {
            { SemanticConventions.Tenant.Tags.RelayTenant, relayTenant?.Value },
            { SemanticConventions.Tenant.Tags.KeyTenant, keyTenant?.Value },
        }));
}
