using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// Test-controllable <see cref="IGrainStorage"/> decorator that throws on
/// <see cref="WriteStateAsync{T}"/> while its <see cref="StorageFaultState.ShouldFailWrites"/>
/// is set, faulting the real substrate's grain-state write so a scenario can prove
/// the framework drops the dirty activation and a redelivery reloads clean durable
/// state. Wraps the substrate's own provider (Postgres / Azure Blob) and forwards
/// <see cref="ILifecycleParticipant{ISiloLifecycle}"/> so the inner provider still
/// initialises during silo start. The fault switch is the
/// <see cref="StorageFaultState"/> the owning fixture passes in: it is per-fixture
/// instance state, so a scenario flips only its own fixture's storage and fixture
/// shapes never race a shared toggle.
/// </summary>
public sealed class ControllableGrainStorage : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
{
    readonly IGrainStorage _inner;
    readonly StorageFaultState _fault;

    public ControllableGrainStorage(IGrainStorage inner, StorageFaultState fault)
    {
        _inner = inner;
        _fault = fault;
    }

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        if (_fault.ShouldFailWrites)
        {
            Interlocked.Increment(ref _fault.FailedWrites);
            throw new InvalidOperationException("Simulated grain-state write fault.");
        }

        return _inner.WriteStateAsync(stateName, grainId, grainState);
    }

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) =>
        _inner.ReadStateAsync(stateName, grainId, grainState);

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) =>
        _inner.ClearStateAsync(stateName, grainId, grainState);

    public void Participate(ISiloLifecycle observer)
    {
        if (_inner is ILifecycleParticipant<ISiloLifecycle> participant)
        {
            participant.Participate(observer);
        }
    }

    /// <summary>
    /// Wraps the <c>edict-state</c> grain-storage provider a fixture has already
    /// registered with this controllable decorator, wired to the fixture's
    /// <paramref name="fault"/> and preserving the inner provider's factory. Call
    /// after the substrate's persistence wiring.
    /// </summary>
    public static void Decorate(IServiceCollection services, StorageFaultState fault, string providerName = "edict-state")
    {
        var original = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IGrainStorage)
            && descriptor.IsKeyedService
            && (string?)descriptor.ServiceKey == providerName);

        var innerFactory = original.KeyedImplementationFactory
            ?? throw new InvalidOperationException(
                $"Grain-storage provider '{providerName}' is not registered via a keyed implementation factory, so it cannot be decorated.");

        services.Remove(original);
        services.AddKeyedSingleton<IGrainStorage>(providerName, (serviceProvider, key) =>
            new ControllableGrainStorage((IGrainStorage)innerFactory(serviceProvider, key), fault));
    }
}
