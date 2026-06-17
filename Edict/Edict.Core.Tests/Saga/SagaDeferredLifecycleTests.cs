using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Sagas;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.Saga;

// The deferred (claim-check pointer-envelope) delivery path: an oversized saga
// event rides the stream as an EdictEventEnvelope pointing at its body in the
// claim-check store, so the receiver stages an InvokeHandler entry and runs the
// handler off the engine drain rather than inline. These tests prove the saga
// lifecycle — cap arming, the terminal guard, and Complete — behaves on that
// path exactly as it does inline.
[Collection(SagaLifecycleCollection.Name)]
public sealed class SagaDeferredLifecycleTests
{
    readonly SagaLifecycleClusterFixture _fixture;

    public SagaDeferredLifecycleTests(SagaLifecycleClusterFixture fixture)
    {
        _fixture = fixture;
        CapturingPublishEventExecutor.Reset();
        CapturingSendCommandExecutor.Reset();
    }

    [Fact]
    public async Task DeferredFirstEvent_ArmsCap()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetCappedSaga(workflowId);

        var armInstant = SagaLifecycleClusterFixture.Time.GetUtcNow();
        await saga.DeliverAsync(PointerEnvelope(Trigger(workflowId)));

        // Arming folds into the staging write even though the body arrived
        // oversized; the handler ran off the drain the staging commit kicked off.
        Assert.Equal(SagaLifecycleState.Live, await saga.GetLifecycleStateAsync());
        Assert.Equal(armInstant.AddMinutes(1), await saga.GetDeadlineAsync());
        Assert.Equal(1, await saga.GetHandledAsync());
    }

    [Fact]
    public async Task DeferredNewEvent_AtTerminalSaga_DeadLetters()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetDefaultCapSaga(workflowId);

        await saga.DeliverAsync(Trigger(workflowId));
        await saga.DeliverAsync(Finish(workflowId));

        // A genuinely-new oversized Event the saga handles, at the now-Completed
        // saga: the body materialises on the drain, the terminal guard recognises
        // it as a handled type, and it dead-letters.
        await saga.DeliverAsync(PointerEnvelope(Trigger(workflowId)));

        Assert.Equal(SagaLifecycleState.Completed, await saga.GetLifecycleStateAsync());
        var deadLetter = Assert.Single(DeadLettersFor(workflowId));
        Assert.Equal(typeof(EdictSagaTerminalException).FullName, deadLetter.ExceptionType);
        // The terminal guard ran instead of the handler, so the counter is unchanged.
        Assert.Equal(2, await saga.GetHandledAsync());
    }

    [Fact]
    public async Task DeferredUnhandledStreamEvent_AtTerminalSaga_IsIgnored()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetDefaultCapSaga(workflowId);

        await saga.DeliverAsync(Trigger(workflowId));
        await saga.DeliverAsync(Finish(workflowId));

        // An oversized foreign event type the saga has no HandleAsync for: the
        // terminal decision happens off the drain where the body is materialised,
        // so an unhandled type is ignored rather than dead-lettered.
        await saga.DeliverAsync(PointerEnvelope(new LifecycleUnrelatedEvent(workflowId) { EventId = Guid.NewGuid() }));

        Assert.Empty(DeadLettersFor(workflowId));
        Assert.Equal(SagaLifecycleState.Completed, await saga.GetLifecycleStateAsync());
        Assert.Equal(2, await saga.GetHandledAsync());
    }

    [Fact]
    public async Task DeferredArmedCap_Fires_AndCompensates()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetCompensatingSaga(workflowId);

        await saga.DeliverAsync(PointerEnvelope(Trigger(workflowId)));
        SagaLifecycleClusterFixture.Time.Advance(TimeSpan.FromMinutes(2));
        await saga.FireCapAsync();

        // A cap armed off the oversized-event staging write fires and compensates
        // exactly as an inline-armed cap does.
        Assert.Equal(SagaLifecycleState.TimedOut, await saga.GetLifecycleStateAsync());
        Assert.Equal(-1, await saga.GetHandledAsync());
        Assert.Single(CompensationsFor(workflowId));
        Assert.Empty(DeadLettersFor(workflowId));
    }

    // The static capture queue is shared by the whole class; filter to this
    // saga's own key so one test never sees another's compensating command.
    static IReadOnlyList<SagaTrackerCommand> CompensationsFor(Guid workflowId) =>
        CapturingSendCommandExecutor.Captured
            .OfType<SagaTrackerCommand>()
            .Where(command => command.WorkflowId == workflowId)
            .ToList();

    [Fact]
    public async Task DeferredComplete_TerminalisesAndDisarms()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetDefaultCapSaga(workflowId);

        await saga.DeliverAsync(Trigger(workflowId));

        // Complete() called by the handler running off the engine drain still
        // terminalises: the Completed write rides the InvokeHandler ack-write.
        await saga.DeliverAsync(PointerEnvelope(Finish(workflowId)));

        Assert.Equal(SagaLifecycleState.Completed, await saga.GetLifecycleStateAsync());
        Assert.Equal(2, await saga.GetHandledAsync());

        // Disarmed: a cap fire against the completed saga is a clean no-op.
        await saga.FireCapAsync();
        Assert.Empty(DeadLettersFor(workflowId));
        Assert.Equal(SagaLifecycleState.Completed, await saga.GetLifecycleStateAsync());
    }

    static LifecycleTriggerEvent Trigger(Guid workflowId) =>
        new(workflowId) { EventId = Guid.NewGuid() };

    static LifecycleFinishEvent Finish(Guid workflowId) =>
        new(workflowId) { EventId = Guid.NewGuid() };

    // The static capture queue is shared by the whole class; filter to this
    // saga's own grain key so one test never sees another's dead-letter.
    static IReadOnlyList<EdictDeadLetterRaised> DeadLettersFor(Guid workflowId) =>
        CapturingPublishEventExecutor.Captured
            .Where(deadLetter => deadLetter.SourceGrainKey == workflowId.ToString("N"))
            .ToList();

    // Serialises the inner event into the shared claim-check store under its own
    // EventId and returns a pointer-bearing wire-frame envelope carrying that
    // same id — the exact shape PublishEventExecutor.Stamp puts on the stream for
    // an oversized event, minus the trace ids the in-process delivery seam does
    // not need.
    EdictEventEnvelope PointerEnvelope(EdictEvent inner)
    {
        var serializer = _fixture.Cluster.Client.ServiceProvider.GetRequiredService<Serializer>();
        var stamped = inner.EventId == Guid.Empty ? inner with { EventId = Guid.NewGuid() } : inner;
        SagaLifecycleClusterFixture.ClaimCheckStore
            .PutAsync(stamped.Tenant, stamped.EventId, serializer.SerializeToArray<EdictEvent>(stamped), CancellationToken.None)
            .GetAwaiter().GetResult();

        return new EdictEventEnvelope(null, stamped.EventId)
        {
            OccurredAt = SagaLifecycleClusterFixture.Time.GetUtcNow(),
            InnerEventStreamName = "SagaLifecycleWorkflow",
            InnerEventRouteKey = Guid.Empty.ToString("N"),
            Tenant = stamped.Tenant,
        };
    }

    ISagaLifecycleProbe GetCappedSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(CappedSaga).FullName);

    ISagaLifecycleProbe GetDefaultCapSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(DefaultCapSaga).FullName);

    ISagaLifecycleProbe GetCompensatingSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(CompensatingSaga).FullName);
}
