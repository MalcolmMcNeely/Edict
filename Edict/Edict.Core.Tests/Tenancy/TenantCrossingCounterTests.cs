using System.Diagnostics.Metrics;

using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.Tenancy;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Runtime;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// The bounded operator signal for the isolation boundary: one counter incremented on
// every real tenant divergence the filter reaches, tagged only by outcome. Its single
// tag is a closed allowlist, never the tenant value, so a breach is alarmable without
// the tenant id leaking into an unbounded metric dimension. The counter carries no
// discriminating tag, so these tests cannot filter their own emissions out of a shared
// process; they run in the sequential listener collection, alone, so every capture is
// theirs.
[Collection(EdictListenerUnitCollection.Name)]
public sealed class TenantCrossingCounterTests : IDisposable
{
    static readonly Guid RouteGuid = Guid.NewGuid();

    public TenantCrossingCounterTests() => RequestContext.Clear();

    public void Dispose() => RequestContext.Clear();

    [Fact]
    public async Task Invoke_ShouldIncrementCrossingCounter_TaggedDenied_WhenCrossingIsDenied()
    {
        OriginIdentity.Seed(null, EdictTenantId.Of("acme"));
        var context = CommandContext(EdictKeyComposer.Compose(EdictTenantId.Of("globex"), RouteGuid.ToString("N")));
        var captures = new List<Capture>();
        using var listener = StartListener(captures);

        await Assert.ThrowsAsync<EdictCrossTenantAccessException>(() => Filter().Invoke(context));

        var capture = Assert.Single(captures);
        Assert.Equal(1L, capture.Value);
        Assert.Equal(
            SemanticConventions.Tenant.Tags.OutcomeValues.Denied,
            capture.Tag(SemanticConventions.Tenant.Tags.Outcome));
    }

    [Fact]
    public async Task Invoke_ShouldIncrementCrossingCounter_TaggedAuthorized_WhenCrossingIsAuthorized()
    {
        OriginIdentity.Seed(null, EdictTenantId.Of("acme"));
        TenantCrossing.Authorize();
        var context = CommandContext(EdictKeyComposer.Compose(EdictTenantId.Of("globex"), RouteGuid.ToString("N")));
        var captures = new List<Capture>();
        using var listener = StartListener(captures);

        await Filter().Invoke(context);

        var capture = Assert.Single(captures);
        Assert.Equal(1L, capture.Value);
        Assert.Equal(
            SemanticConventions.Tenant.Tags.OutcomeValues.Authorized,
            capture.Tag(SemanticConventions.Tenant.Tags.Outcome));
    }

    [Fact]
    public async Task Invoke_ShouldTagCrossingCounterWithOutcomeOnly_NeverTheTenantValue()
    {
        OriginIdentity.Seed(null, EdictTenantId.Of("acme"));
        var context = CommandContext(EdictKeyComposer.Compose(EdictTenantId.Of("globex"), RouteGuid.ToString("N")));
        var captures = new List<Capture>();
        using var listener = StartListener(captures);

        await Assert.ThrowsAsync<EdictCrossTenantAccessException>(() => Filter().Invoke(context));

        var capture = Assert.Single(captures);
        var tagKey = Assert.Single(capture.Tags.Keys);
        Assert.Equal(SemanticConventions.Tenant.Tags.Outcome, tagKey);
        Assert.DoesNotContain(capture.Tags.Values, value => value is "acme" or "globex");
    }

    static EdictTenantIsolationCallFilter Filter() =>
        new(new ServiceCollection().BuildServiceProvider());

    static FakeIncomingGrainCallContext CommandContext(string grainKey) =>
        new(new FakeCommandHandlerGrain(), GrainId.Create("employee", grainKey));

    static MeterListener StartListener(List<Capture> captures)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, enableFor) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.Tenant.Meters.CrossingCount)
                {
                    enableFor.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var snapshot = new Dictionary<string, object?>(tags.Length);
            foreach (var tag in tags)
            {
                snapshot[tag.Key] = tag.Value;
            }
            captures.Add(new Capture(value, snapshot));
        });
        listener.Start();
        return listener;
    }

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var value) ? value : null;
    }
}
