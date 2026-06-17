using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Streams;

namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// Flippable executor used by the outbox conformance scenarios to simulate a
/// crash between the ring/outbox commit and an effect's execution, then drive a
/// recovery drain against the bound substrate. Wraps a real inner executor and
/// delegates to it when not failing, so a successful drain actually publishes to
/// the stream or relays the command. It deliberately leaves <c>TryResolveBatchKey</c>
/// and <c>ExecuteBatchAsync</c> at their interface defaults, so every entry lands
/// in a singleton group and flows through <see cref="ExecuteAsync"/> independently
/// — that per-entry path is what lets a scenario fault one effect in a batch and
/// leave its siblings done. The fault switch is the <see cref="OutboxFaultState"/>
/// the owning fixture passes in: it is per-fixture instance state, so a scenario
/// flips only its own fixture's executor and fixture shapes never race a shared
/// toggle.
/// </summary>
public sealed class ControllableOutboxExecutor : IOutboxEffectExecutor
{
    readonly IOutboxEffectExecutor _inner;
    readonly OutboxFaultState _fault;

    ControllableOutboxExecutor(IOutboxEffectExecutor inner, OutboxFaultState fault)
    {
        _inner = inner;
        _fault = fault;
    }

    public OutboxEffectKind Kind => _inner.Kind;

    public Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
    {
        if (ShouldFault(entry))
        {
            Interlocked.Increment(ref _fault.FailedAttempts);
            throw BuildFailure();
        }

        return _inner.ExecuteAsync(entry, streamProvider, deferredDispatch, consumerType, liveWireEvent);
    }

    bool ShouldFault(OutboxEntry entry)
    {
        if (_fault.FailOnlyKind is { } onlyKind && entry.Kind != onlyKind)
        {
            return false;
        }

        if (_fault.FailEffectIndex is { } targetIndex)
        {
            var ordinal = Interlocked.Increment(ref _fault.EffectsSeen) - 1;
            return ordinal == targetIndex;
        }

        if (_fault.FailUntilAttempt is { } attemptsToFail)
        {
            return _fault.FailedAttempts < attemptsToFail;
        }

        return _fault.ShouldFail;
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
    /// Removes the real <see cref="PublishEventExecutor"/> and
    /// <see cref="SendCommandExecutor"/> <c>AddEdict</c> registered and registers a
    /// controllable wrapper for each in their place, both wired to the fixture's
    /// <paramref name="fault"/>. Appending instead of replacing would make the
    /// outbox host's keyed lookup on <see cref="OutboxEffectKind"/> throw on the
    /// duplicate key.
    /// </summary>
    public static void Replace(IServiceCollection services, OutboxFaultState fault)
    {
        ReplaceKind<PublishEventExecutor>(services, fault);
        ReplaceKind<SendCommandExecutor>(services, fault);
    }

    static void ReplaceKind<TExecutor>(IServiceCollection services, OutboxFaultState fault)
        where TExecutor : IOutboxEffectExecutor
    {
        var registration = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IOutboxEffectExecutor)
            && descriptor.ImplementationType == typeof(TExecutor));
        services.Remove(registration);
        services.AddSingleton<IOutboxEffectExecutor>(serviceProvider =>
            new ControllableOutboxExecutor(ActivatorUtilities.CreateInstance<TExecutor>(serviceProvider), fault));
    }
}

public enum ControllableFailureKind
{
    InvalidOperation,
    UnregisteredEvent,
    SagaCoordination,
}
