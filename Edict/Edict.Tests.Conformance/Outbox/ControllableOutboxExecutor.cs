using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// Flippable <see cref="OutboxEffectKind.PublishEvent"/> executor used by the
/// outbox conformance scenarios to simulate a crash between the ring/outbox
/// commit and the publish, then drive a recovery drain against the bound
/// substrate. Delegates to the real <see cref="PublishEventExecutor"/> when
/// not failing, so a successful drain actually publishes to the stream. The
/// fault switch is the <see cref="OutboxFaultState"/> the owning fixture passes
/// in: it is per-fixture instance state, so a scenario flips only its own
/// fixture's executor and fixture shapes never race a shared toggle.
/// </summary>
public sealed class ControllableOutboxExecutor : IOutboxEffectExecutor
{
    readonly PublishEventExecutor _inner;
    readonly OutboxFaultState _fault;

    public ControllableOutboxExecutor(IServiceProvider serviceProvider, OutboxFaultState fault)
    {
        _inner = ActivatorUtilities.CreateInstance<PublishEventExecutor>(serviceProvider);
        _fault = fault;
    }

    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
    {
        if (_fault.ShouldFail)
        {
            Interlocked.Increment(ref _fault.FailedAttempts);
            throw BuildFailure();
        }

        return _inner.ExecuteAsync(entry, streamProvider, deferredDispatch, consumerType, liveWireEvent);
    }

    Exception BuildFailure() => _fault.FailureKind switch
    {
        ControllableFailureKind.UnregisteredEvent => new EdictUnregisteredTypeException(
            EdictUnregisteredTypeException.Kind.Event,
            "Conformance.Unregistered.SyntheticEvent",
            "controllable publish failure (unregistered event)"),
        ControllableFailureKind.SagaCoordination => new EdictSagaCoordinationException(
            "controllable publish failure (saga dispatched twice)"),
        _ => new InvalidOperationException("controllable publish failure (outbox conformance test)"),
    };

    /// <summary>
    /// Removes the <see cref="PublishEventExecutor"/> <c>AddEdict</c> registered
    /// and registers this controllable executor in its place, wired to the
    /// fixture's <paramref name="fault"/>. Appending instead of replacing would
    /// make the outbox host's keyed lookup on <see cref="OutboxEffectKind"/>
    /// throw on the duplicate <see cref="OutboxEffectKind.PublishEvent"/> key.
    /// </summary>
    public static void Replace(IServiceCollection services, OutboxFaultState fault)
    {
        var publish = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IOutboxEffectExecutor)
            && descriptor.ImplementationType == typeof(PublishEventExecutor));
        services.Remove(publish);
        services.AddSingleton<IOutboxEffectExecutor>(serviceProvider =>
            new ControllableOutboxExecutor(serviceProvider, fault));
    }
}

public enum ControllableFailureKind
{
    InvalidOperation,
    UnregisteredEvent,
    SagaCoordination,
}
