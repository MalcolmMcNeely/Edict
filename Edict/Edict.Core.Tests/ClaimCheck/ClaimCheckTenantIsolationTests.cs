using Edict.Contracts.Events;
using Edict.Contracts.Tenancy;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using Xunit;

namespace Edict.Core.Tests.ClaimCheck;

// The claim-check store holds the largest, most sensitive spilled payloads, so a
// tenant-scoped body must never be addressable from another tenant's wall. The
// store folds the tenant into its backing key: the same EventId under two tenants
// is two distinct parked bodies, and a public (null-tenant) event keeps the bare
// key. Provider-invariant — proven once against the in-memory store; the real
// stores conform to the same contract under the conformance battery.
public sealed class ClaimCheckTenantIsolationTests
{
    [Fact]
    public async Task PutThenGet_UnderSameTenant_ShouldReturnIdenticalBytes()
    {
        // Arrange
        var store = new InMemoryClaimCheckStore();
        var tenant = EdictTenantId.Of("acme");
        var eventId = Guid.NewGuid();
        byte[] payload = [0x10, 0x20, 0x30];

        // Act
        await store.PutAsync(tenant, eventId, payload, CancellationToken.None);
        var fetched = await store.GetAsync(tenant, eventId, CancellationToken.None);

        // Assert
        Assert.Equal(payload, fetched.ToArray());
    }

    [Fact]
    public async Task Get_UnderDifferentTenant_ShouldMiss_WhenBodyParkedUnderAnotherTenant()
    {
        // Arrange
        var store = new InMemoryClaimCheckStore();
        var owningTenant = EdictTenantId.Of("acme");
        var intruderTenant = EdictTenantId.Of("globex");
        var eventId = Guid.NewGuid();
        byte[] payload = [0x10, 0x20, 0x30];
        await store.PutAsync(owningTenant, eventId, payload, CancellationToken.None);

        // Act + Assert
        await Assert.ThrowsAsync<EdictClaimCheckFetchException>(
            () => store.GetAsync(intruderTenant, eventId, CancellationToken.None));
    }

    [Fact]
    public async Task PutThenGet_ForPublicEvent_ShouldRoundTrip_WhenNoTenant()
    {
        // Arrange
        var store = new InMemoryClaimCheckStore();
        var eventId = Guid.NewGuid();
        byte[] payload = [0x10, 0x20, 0x30];

        // Act
        await store.PutAsync(tenant: null, eventId, payload, CancellationToken.None);
        var fetched = await store.GetAsync(tenant: null, eventId, CancellationToken.None);

        // Assert
        Assert.Equal(payload, fetched.ToArray());
    }

    [Fact]
    public async Task OversizedEvent_SpilledThenUnwrapped_ShouldRoundTripWithinItsOwnTenant()
    {
        // Arrange: an oversized tenant-scoped event takes the claim-check pointer path.
        var store = new InMemoryClaimCheckStore();
        var tenant = EdictTenantId.Of("acme");
        var inner = new OrderPlacedEvent(Guid.NewGuid(), new string('x', 256))
        {
            EventId = Guid.NewGuid(),
            Tenant = tenant,
        };
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 64, store, new StubEdictEventStreamAccessors());
        var unwrap = new ClaimCheckUnwrap(Serializer, store);

        // Act: spill, then fetch back through the pointer envelope.
        var spilled = await policy.ApplyAsync(inner, default, CancellationToken.None);
        var envelope = Assert.IsType<EdictEventEnvelope>(spilled.WireEvent);
        var materialised = await unwrap.ApplyAsync(envelope, consumerType: typeof(object), parentContext: default, CancellationToken.None);

        // Assert: the wall rides the wire so the receiver fetches its own tenant's body.
        Assert.Equal(tenant, envelope.Tenant);
        Assert.Equal(inner.EventId, materialised.EventId);
    }

    [Fact]
    public async Task OversizedEvent_SpilledUnderOneTenant_ShouldBeUnreachable_FromAnotherTenantsWall()
    {
        // Arrange: tenant A spills an oversized body.
        var store = new InMemoryClaimCheckStore();
        var inner = new OrderPlacedEvent(Guid.NewGuid(), new string('x', 256))
        {
            EventId = Guid.NewGuid(),
            Tenant = EdictTenantId.Of("acme"),
        };
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 64, store, new StubEdictEventStreamAccessors());
        var unwrap = new ClaimCheckUnwrap(Serializer, store);
        var spilled = await policy.ApplyAsync(inner, default, CancellationToken.None);
        var acmeEnvelope = Assert.IsType<EdictEventEnvelope>(spilled.WireEvent);

        // A forged envelope at the same EventId but carrying a different wall.
        var intruderEnvelope = acmeEnvelope with { Tenant = EdictTenantId.Of("globex") };

        // Act + Assert: the body is parked behind acme's wall, not globex's.
        await Assert.ThrowsAsync<EdictClaimCheckFetchException>(
            () => unwrap.ApplyAsync(intruderEnvelope, consumerType: typeof(object), parentContext: default, CancellationToken.None));
    }

    static readonly Serializer Serializer = BuildSerializer();

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder =>
        {
            builder.AddAssembly(typeof(ClaimCheckTenantIsolationTests).Assembly);
            builder.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }
}
