using System.Diagnostics;

using Edict.Contracts.Audit;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.Audit;
using Edict.Core.Commands;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
/// unless the crossing was explicitly authorized, in which case it proceeds, records
/// the crossing as a span event, and — when auditing is on — lands a standalone audit
/// row so the privileged crossing is never silent in the crossed wall's own trail.
/// Projection grains ride streams, not direct calls, and are isolated structurally by
/// their composed key, so the filter leaves them untouched.
/// </summary>
sealed class EdictTenantIsolationCallFilter(IServiceProvider serviceProvider) : IIncomingGrainCallFilter
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
            await RecordCrossingAuditRowAsync(context.Grain.GetType(), grainKey, keyTenant);
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

    // Lands one self-sealed audit row for an authorized crossing, attributed to the
    // operator who crossed and scoped to the wall crossed into, so the privileged
    // crossing shows up in that tenant's own trail alongside the span event. The filter
    // is not a grain and holds no per-aggregate chain head, so each crossing is a
    // genesis-sealed standalone row, tamper-evident on its own content rather than
    // linked into a sequence. Best-effort: a missing store means auditing is off (span
    // event only), and a store fault never fails the authorized call it rode in on.
    async Task RecordCrossingAuditRowAsync(Type grainType, string grainKey, EdictTenantId? keyTenant)
    {
        var store = serviceProvider.GetService<IEdictAuditStore>();
        if (store is null)
        {
            return;
        }

        try
        {
            var timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
            var content = new EdictAuditRecord
            {
                RecordId = Guid.NewGuid(),
                Kind = EdictAuditKind.TenantCrossing,
                Principal = PrincipalRelay.Current(),
                Tenant = keyTenant,
                EntityType = grainType.FullName ?? grainType.Name,
                EntityKey = grainKey,
                OccurredAt = timeProvider.GetUtcNow(),
            };
            var sealedRecord = AuditRecordBuilder.Seal(content, HashChain.Genesis, sequence: 0);
            await store.AppendAsync([sealedRecord], CancellationToken.None);
        }
        catch (Exception exception)
        {
            serviceProvider.GetService<ILogger<EdictTenantIsolationCallFilter>>()?.LogWarning(
                exception, "Failed to record the audit row for an authorized cross-tenant crossing into {KeyTenant}.", keyTenant?.Value);
        }
    }
}
