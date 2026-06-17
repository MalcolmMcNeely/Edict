using System.Diagnostics;

using Edict.Contracts.Audit;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.Tenancy;
using Edict.Core.Tests.Audit;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Runtime;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// The runtime backstop. Cross-tenant addressing is structurally impossible on the
// common path because every key is composed from the ambient tenant; this filter
// catches the residue — a coding bug that formed a key with a tenant other than the
// turn's, or an illegitimate reach into another wall. It parses the tenant from the
// grain's own key and compares it to the relay's ambient tenant: silent when they
// agree, throwing when they diverge, unless the crossing was explicitly authorized.
public sealed class EdictTenantIsolationCallFilterTests : IDisposable
{
    static readonly Guid RouteGuid = Guid.NewGuid();

    public EdictTenantIsolationCallFilterTests() => RequestContext.Clear();

    public void Dispose() => RequestContext.Clear();

    [Fact]
    public async Task Invoke_ShouldProceedSilently_WhenKeyTenantEqualsRelayTenant()
    {
        OriginIdentity.Seed(null, EdictTenantId.Of("acme"));
        var context = CommandContext(EdictKeyComposer.Compose(EdictTenantId.Of("acme"), RouteGuid.ToString("N")));

        await FilterWithoutAudit().Invoke(context);

        Assert.True(context.Invoked);
    }

    [Fact]
    public async Task Invoke_ShouldThrow_WhenKeyTenantDiffersFromRelayTenant()
    {
        OriginIdentity.Seed(null, EdictTenantId.Of("acme"));
        var context = CommandContext(EdictKeyComposer.Compose(EdictTenantId.Of("globex"), RouteGuid.ToString("N")));

        await Assert.ThrowsAsync<EdictCrossTenantAccessException>(() =>
            FilterWithoutAudit().Invoke(context));
        Assert.False(context.Invoked);
    }

    [Fact]
    public async Task Invoke_ShouldProceedAndRecordSpanEvent_WhenCrossingIsAuthorized()
    {
        OriginIdentity.Seed(null, EdictTenantId.Of("acme"));
        TenantCrossing.Authorize();
        var context = CommandContext(EdictKeyComposer.Compose(EdictTenantId.Of("globex"), RouteGuid.ToString("N")));

        using var listener = ListenForActivities();
        using var activity = EdictDiagnostics.ActivitySource.StartActivity("test.crossing");

        await FilterWithoutAudit().Invoke(context);

        Assert.True(context.Invoked);
        Assert.Contains(activity!.Events, recorded => recorded.Name == SemanticConventions.Tenant.Events.CrossTenantAuthorized);
    }

    [Fact]
    public async Task Invoke_ShouldLandACrossingAuditRow_WhenCrossingIsAuthorizedAndAuditingIsOn()
    {
        OriginIdentity.Seed(EdictPrincipal.Of("operator-9"), null);
        TenantCrossing.Authorize();
        var grainKey = EdictKeyComposer.Compose(EdictTenantId.Of("globex"), RouteGuid.ToString("N"));
        var context = CommandContext(grainKey);
        var store = new RecordingAuditStore();

        await FilterWith(store).Invoke(context);

        // The crossing lands one row in the crossed wall's trail, attributed to the
        // operator who crossed, kinded as a tenant crossing.
        var record = Assert.Single(await store.ByEntityAsync(typeof(FakeCommandHandlerGrain).FullName!, grainKey, CancellationToken.None));
        Assert.Equal(EdictAuditKind.TenantCrossing, record.Kind);
        Assert.Equal(EdictTenantId.Of("globex"), record.Tenant);
        Assert.Equal(EdictPrincipal.Of("operator-9"), record.Principal);
    }

    [Fact]
    public async Task Invoke_ShouldNotLandACrossingAuditRow_WhenCrossingIsDenied()
    {
        OriginIdentity.Seed(EdictPrincipal.Of("operator-9"), EdictTenantId.Of("acme"));
        var grainKey = EdictKeyComposer.Compose(EdictTenantId.Of("globex"), RouteGuid.ToString("N"));
        var context = CommandContext(grainKey);
        var store = new RecordingAuditStore();

        await Assert.ThrowsAsync<EdictCrossTenantAccessException>(() => FilterWith(store).Invoke(context));

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task Invoke_ShouldProceedSilently_WhenGrainIsPublicAggregate()
    {
        OriginIdentity.Seed(null, null);
        var context = CommandContext(EdictKeyComposer.Compose(null, RouteGuid.ToString("N")));

        await FilterWithoutAudit().Invoke(context);

        Assert.True(context.Invoked);
    }

    [Fact]
    public async Task Invoke_ShouldProceedSilently_WhenGrainIsNotACommandHandler()
    {
        OriginIdentity.Seed(null, EdictTenantId.Of("acme"));
        var tenantKey = EdictKeyComposer.Compose(EdictTenantId.Of("globex"), RouteGuid.ToString("N"));
        var context = new FakeIncomingGrainCallContext(new FakeProjectionGrain(), GrainId.Create("projection", tenantKey));

        await FilterWithoutAudit().Invoke(context);

        Assert.True(context.Invoked);
    }

    static EdictTenantIsolationCallFilter FilterWithoutAudit() =>
        new(new ServiceCollection().BuildServiceProvider());

    static EdictTenantIsolationCallFilter FilterWith(IEdictAuditStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        return new EdictTenantIsolationCallFilter(services.BuildServiceProvider());
    }

    static FakeIncomingGrainCallContext CommandContext(string grainKey) =>
        new(new FakeCommandHandlerGrain(), GrainId.Create("employee", grainKey));

    static ActivityListener ListenForActivities()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
