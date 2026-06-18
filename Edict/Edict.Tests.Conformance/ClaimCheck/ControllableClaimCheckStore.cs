using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Tenancy;
using Edict.Core.DeadLetter;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// Fault-injecting <see cref="IEdictClaimCheckStore"/> decorator: the claim-check
/// analogue of <c>ControllableOutboxExecutor</c>. Wraps a real store and faults
/// the receiver-side <see cref="GetAsync"/> a count-addressed number of times
/// (raising the same typed <see cref="EdictClaimCheckFetchException"/> the
/// production stores raise on an absent body) before delegating, so a scenario can
/// prove the transient-recovery path — the body is present and the engine's
/// per-entry retry eventually fetches and delivers it — is distinct from the
/// permanent-missing path that promotes to dead-letter. <see cref="PutAsync"/>
/// always delegates: the spill is never the fault, only the fetch. The fault
/// switch is the <see cref="ClaimCheckFaultState"/> the owning fixture passes in,
/// so a scenario flips only its own fixture's store and fixture shapes never race
/// a shared toggle.
/// </summary>
public sealed class ControllableClaimCheckStore : IEdictClaimCheckStore
{
    readonly IEdictClaimCheckStore _inner;
    readonly ClaimCheckFaultState _fault;

    ControllableClaimCheckStore(IEdictClaimCheckStore inner, ClaimCheckFaultState fault)
    {
        _inner = inner;
        _fault = fault;
    }

    public Task PutAsync(EdictTenantId? tenant, Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        _inner.PutAsync(tenant, eventId, payload, cancellationToken);

    public Task<ReadOnlyMemory<byte>> GetAsync(EdictTenantId? tenant, Guid eventId, CancellationToken cancellationToken)
    {
        // A targeted fault passes every other key's fetch through untouched, so a
        // peer receiver's fetch never consumes the count-addressed fault.
        var faultsThisKey = _fault.FailFetchForEvent is null || _fault.FailFetchForEvent == eventId;
        if (faultsThisKey && _fault.FailFetchUntilAttempt is { } fetchesToFail && _fault.FailedFetches < fetchesToFail)
        {
            Interlocked.Increment(ref _fault.FailedFetches);
            throw new EdictClaimCheckFetchException(
                eventId,
                $"controllable transient fetch failure for event '{eventId:N}' (claim-check conformance test).");
        }

        return _inner.GetAsync(tenant, eventId, cancellationToken);
    }

    /// <summary>
    /// Replaces the <see cref="IEdictClaimCheckStore"/> a fixture has already
    /// registered with a controllable wrapper over it, wired to the fixture's
    /// <paramref name="fault"/>. Handles both an instance registration (the Azure
    /// fixture, which registers its pre-built store) and a factory registration
    /// (the Postgres persistence extension), so one decorator serves both axes.
    /// Call after the substrate's claim-check wiring.
    /// </summary>
    public static void Decorate(IServiceCollection services, ClaimCheckFaultState fault)
    {
        var original = services.Last(descriptor => descriptor.ServiceType == typeof(IEdictClaimCheckStore));
        services.Remove(original);

        if (original.ImplementationInstance is IEdictClaimCheckStore instance)
        {
            services.AddSingleton<IEdictClaimCheckStore>(new ControllableClaimCheckStore(instance, fault));
            return;
        }

        var innerFactory = original.ImplementationFactory
            ?? throw new InvalidOperationException(
                "The claim-check store is registered neither as an instance nor a factory, so it cannot be decorated.");
        services.AddSingleton<IEdictClaimCheckStore>(serviceProvider =>
            new ControllableClaimCheckStore((IEdictClaimCheckStore)innerFactory(serviceProvider), fault));
    }
}
