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
/// not failing, so a successful drain actually publishes to the stream.
/// The static <see cref="ShouldFail"/> flag is process-wide; every concrete
/// fixture that wires this executor must serialise its tests via an xUnit
/// collection so the toggle does not race across fixture shapes.
/// </summary>
public sealed class ControllableOutboxExecutor : IOutboxEffectExecutor
{
    readonly PublishEventExecutor _inner;

    public ControllableOutboxExecutor(IServiceProvider serviceProvider)
    {
        _inner = ActivatorUtilities.CreateInstance<PublishEventExecutor>(serviceProvider);
    }

    public static volatile bool ShouldFail;
    public static int FailedAttempts;

    /// <summary>
    /// Selects the exception kind raised on a failing pass. Defaults to the
    /// historical <see cref="InvalidOperationException"/> so existing scenarios
    /// stay green; new scenarios that need to verify the classifier-to-bucket
    /// mapping for a typed runtime fault switch this to the relevant kind.
    /// </summary>
    public static volatile ControllableFailureKind FailureKind = ControllableFailureKind.InvalidOperation;

    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
    {
        if (ShouldFail)
        {
            Interlocked.Increment(ref FailedAttempts);
            throw BuildFailure();
        }

        return _inner.ExecuteAsync(entry, streamProvider, deferredDispatch, consumerType, liveWireEvent);
    }

    static Exception BuildFailure() => FailureKind switch
    {
        ControllableFailureKind.UnregisteredEvent => new EdictUnregisteredTypeException(
            EdictUnregisteredTypeException.Kind.Event,
            "Conformance.Unregistered.SyntheticEvent",
            "controllable publish failure (unregistered event)"),
        ControllableFailureKind.SagaCoordination => new EdictSagaCoordinationException(
            "controllable publish failure (saga dispatched twice)"),
        _ => new InvalidOperationException("controllable publish failure (outbox conformance test)"),
    };

    public static void Reset()
    {
        ShouldFail = false;
        FailedAttempts = 0;
        FailureKind = ControllableFailureKind.InvalidOperation;
    }
}

public enum ControllableFailureKind
{
    InvalidOperation,
    UnregisteredEvent,
    SagaCoordination,
}
