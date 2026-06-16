using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Core.Sagas;
using Edict.Telemetry;

namespace Edict.Core.Tests.Saga;

// In-memory TestCluster lifecycle battery (ADR 0016: mechanism logic, in-memory
// only — no Azurite). Events drive through the in-process delivery seam and the
// cap fires through a probe, so the FakeTimeProvider can be advanced
// deterministically without waiting on Orleans' one-minute reminder floor.
[Collection(SagaLifecycleCollection.Name)]
public sealed class SagaLifecycleTests
{
    readonly SagaLifecycleClusterFixture _fixture;

    public SagaLifecycleTests(SagaLifecycleClusterFixture fixture)
    {
        _fixture = fixture;
        CapturingPublishEventExecutor.Reset();
        CapturingSendCommandExecutor.Reset();
    }

    static LifecycleTriggerEvent Trigger(Guid workflowId) =>
        new(workflowId) { EventId = Guid.NewGuid() };

    static LifecycleFinishEvent Finish(Guid workflowId) =>
        new(workflowId) { EventId = Guid.NewGuid() };

    // The static capture queue is shared by the whole class; filter to this
    // saga's own grain key so one test never sees another's dead-letter.
    static IReadOnlyList<EdictDeadLetterRaised> DeadLettersFor(Guid workflowId) =>
        CapturingPublishEventExecutor.Captured
            .Where(deadLetter => deadLetter.SourceGrainKey == workflowId.ToString())
            .ToList();

    [Fact]
    public async Task CapArmsOnFirstHandle_WithExplicitAttributeCap()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetCappedSaga(workflowId);

        var armInstant = SagaLifecycleClusterFixture.Time.GetUtcNow();
        await saga.DeliverAsync(Trigger(workflowId));

        // Arrange / Act above; the cap is the [EdictSagaTimeout("00:01:00")] on CappedSaga.
        Assert.Equal(SagaLifecycleState.Live, await saga.GetLifecycleStateAsync());
        Assert.Equal(armInstant.AddMinutes(1), await saga.GetDeadlineAsync());
    }

    [Fact]
    public async Task CapArmsOnFirstHandle_WithSiloWideDefault()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetDefaultCapSaga(workflowId);

        var armInstant = SagaLifecycleClusterFixture.Time.GetUtcNow();
        await saga.DeliverAsync(Trigger(workflowId));

        // DefaultCapSaga carries no attribute, so it inherits the 7-day default.
        Assert.Equal(armInstant.AddDays(7), await saga.GetDeadlineAsync());
    }

    [Fact]
    public async Task Complete_TerminalisesAndDisarms()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetDefaultCapSaga(workflowId);

        await saga.DeliverAsync(Trigger(workflowId));
        await saga.DeliverAsync(Finish(workflowId));

        Assert.Equal(SagaLifecycleState.Completed, await saga.GetLifecycleStateAsync());
        Assert.Equal(2, await saga.GetHandledAsync());

        // Disarmed: a cap fire against the completed saga is a clean no-op.
        await saga.FireCapAsync();
        Assert.Empty(DeadLettersFor(workflowId));
        Assert.Equal(SagaLifecycleState.Completed, await saga.GetLifecycleStateAsync());
    }

    [Fact]
    public async Task FiredCap_WithNoOverride_DeadLetters()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetCappedSaga(workflowId);

        await saga.DeliverAsync(Trigger(workflowId));
        SagaLifecycleClusterFixture.Time.Advance(TimeSpan.FromMinutes(2));
        await saga.FireCapAsync();

        Assert.Equal(SagaLifecycleState.TimedOut, await saga.GetLifecycleStateAsync());
        var deadLetter = Assert.Single(DeadLettersFor(workflowId));
        Assert.Equal(typeof(EdictSagaTimeoutException).FullName, deadLetter.ExceptionType);
        // The saga lifecycle dead-letter originates its own delivery identity at
        // BuildSagaDeadLetterEntry — the drain no longer mints one, and a fresh id
        // keeps each lifecycle dead-letter a distinct row in the singleton ring.
        Assert.NotEqual(Guid.Empty, deadLetter.EventId);
    }

    // The static capture queue is shared by the whole class; filter to this
    // saga's own key so one test never sees another's compensating command.
    static IReadOnlyList<SagaTrackerCommand> CompensationsFor(Guid workflowId) =>
        CapturingSendCommandExecutor.Captured
            .OfType<SagaTrackerCommand>()
            .Where(command => command.WorkflowId == workflowId)
            .ToList();

    [Fact]
    public async Task FiredCap_WithOverride_CompensatesAndTimesOut()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetCompensatingSaga(workflowId);

        await saga.DeliverAsync(Trigger(workflowId));
        SagaLifecycleClusterFixture.Time.Advance(TimeSpan.FromMinutes(2));
        await saga.FireCapAsync();

        // The fired cap ran the override: the saga is terminal, the hook's
        // Progress mutation is durable, and exactly one compensating Command was
        // dispatched — all observed through the durable outcome, not internals.
        Assert.Equal(SagaLifecycleState.TimedOut, await saga.GetLifecycleStateAsync());
        Assert.Equal(-1, await saga.GetHandledAsync());
        Assert.Single(CompensationsFor(workflowId));

        // An override compensates; it does not also dead-letter.
        Assert.Empty(DeadLettersFor(workflowId));
    }

    [Fact]
    public async Task FiredCap_WithThrowingOverride_ContainsThrow_DeadLettersAsConsumerBug_AndFiresOnce()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetThrowingTimeoutSaga(workflowId);

        await saga.DeliverAsync(Trigger(workflowId));
        SagaLifecycleClusterFixture.Time.Advance(TimeSpan.FromMinutes(2));

        // The throwing override must not escape the cap handler: this returning at
        // all (rather than rethrowing the consumer's InvalidOperationException) is
        // the regression assertion.
        await saga.FireCapAsync();

        // Terminalised despite the throw, and the half-applied Progress mutation the
        // hook made before throwing was rolled back to the durable trigger value.
        Assert.Equal(SagaLifecycleState.TimedOut, await saga.GetLifecycleStateAsync());
        Assert.Equal(1, await saga.GetHandledAsync());

        // The compensating Command the hook dispatched before throwing never reaches
        // the outbox: a contained throw discards the whole turn's buffered effect.
        Assert.Empty(CompensationsFor(workflowId));

        // The dead-letter distinguishes "your compensation threw" (ConsumerBug,
        // EdictSagaCompensationException) from the by-design no-override SagaTimeout,
        // and preserves the real thrown exception's type and message in Reason.
        var deadLetter = Assert.Single(DeadLettersFor(workflowId));
        Assert.Equal(typeof(EdictSagaCompensationException).FullName, deadLetter.ExceptionType);
        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.ConsumerBug, deadLetter.Kind);
        Assert.Contains(typeof(InvalidOperationException).FullName!, deadLetter.Reason);
        Assert.Contains(ThrowingTimeoutSaga.ThrownMessage, deadLetter.Reason);

        // The cap fires exactly once: the reminder is unregistered, and a second
        // tick against the now-TimedOut saga is a clean no-op with no second
        // dead-letter and no escaping throw.
        await saga.FireCapAsync();
        Assert.Single(DeadLettersFor(workflowId));
    }

    [Fact]
    public async Task DoubleCapFire_WithOverride_CompensatesOnce()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetCompensatingSaga(workflowId);

        await saga.DeliverAsync(Trigger(workflowId));
        SagaLifecycleClusterFixture.Time.Advance(TimeSpan.FromMinutes(2));

        await saga.FireCapAsync();
        await saga.FireCapAsync();

        // The terminal-state guard from slice 1 holds for the compensation path:
        // the second fire is a clean no-op against the now-TimedOut saga.
        Assert.Single(CompensationsFor(workflowId));
        Assert.Equal(SagaLifecycleState.TimedOut, await saga.GetLifecycleStateAsync());
    }

    [Fact]
    public async Task TerminalSaga_DeadLettersNewEvent_ButSuppressesRedelivery()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetDefaultCapSaga(workflowId);

        var firstTrigger = Trigger(workflowId);
        await saga.DeliverAsync(firstTrigger);
        await saga.DeliverAsync(Finish(workflowId));

        // A genuinely-new Event at the now-Completed saga dead-letters.
        await saga.DeliverAsync(Trigger(workflowId));

        // A plain redelivery of an already-handled Event is still suppressed by
        // the dedup ring (checked before the terminal rule).
        await saga.DeliverAsync(firstTrigger);

        var deadLetter = Assert.Single(DeadLettersFor(workflowId));
        Assert.Equal(typeof(EdictSagaTerminalException).FullName, deadLetter.ExceptionType);
        Assert.Equal(2, await saga.GetHandledAsync());
    }

    [Fact]
    public async Task TerminalSaga_IgnoresUnhandledStreamEvent_WithoutDeadLettering()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetDefaultCapSaga(workflowId);

        await saga.DeliverAsync(Trigger(workflowId));
        await saga.DeliverAsync(Finish(workflowId));

        // A foreign event type the saga receives off the shared stream but has no
        // HandleAsync for. Live, it is silently ignored; terminal, it must still be
        // ignored: only events the saga actually handles dead-letter at a terminal
        // saga.
        await saga.DeliverAsync(new LifecycleUnrelatedEvent(workflowId) { EventId = Guid.NewGuid() });

        Assert.Empty(DeadLettersFor(workflowId));
        Assert.Equal(SagaLifecycleState.Completed, await saga.GetLifecycleStateAsync());
        Assert.Equal(2, await saga.GetHandledAsync());
    }

    [Fact]
    public async Task DoubleCapFire_IsCleanNoOp()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetCappedSaga(workflowId);

        await saga.DeliverAsync(Trigger(workflowId));
        SagaLifecycleClusterFixture.Time.Advance(TimeSpan.FromMinutes(2));

        await saga.FireCapAsync();
        await saga.FireCapAsync();

        Assert.Single(DeadLettersFor(workflowId));
        Assert.Equal(SagaLifecycleState.TimedOut, await saga.GetLifecycleStateAsync());
    }

    [Fact]
    public async Task DeadlineAt_SurvivesReactivation()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetCappedSaga(workflowId);

        await saga.DeliverAsync(Trigger(workflowId));
        var deadlineBefore = await saga.GetDeadlineAsync();

        await saga.RequestDeactivationAsync();

        var deadlineAfter = await saga.GetDeadlineAsync();

        Assert.NotNull(deadlineAfter);
        Assert.Equal(deadlineBefore, deadlineAfter);
    }

    [Fact]
    public async Task DispatchNothingHandle_ProgressMutation_SurvivesReactivation()
    {
        var workflowId = Guid.NewGuid();
        var saga = GetCappedSaga(workflowId);

        // A handler that mutates Progress and dispatches no Command is a valid
        // handle, not a dropped one: the mutation rides the same atomic
        // dedup-ring commit, so it must outlive reactivation. The same-activation
        // Handled assertions elsewhere don't prove that durability.
        await saga.DeliverAsync(Trigger(workflowId));
        var handledBefore = await saga.GetHandledAsync();

        await saga.RequestDeactivationAsync();

        var handledAfter = await saga.GetHandledAsync();

        Assert.Empty(CapturingSendCommandExecutor.Captured);
        Assert.Equal(1, handledBefore);
        Assert.Equal(handledBefore, handledAfter);
    }

    ISagaLifecycleProbe GetDefaultCapSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(DefaultCapSaga).FullName);

    ISagaLifecycleProbe GetCappedSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(CappedSaga).FullName);

    ISagaLifecycleProbe GetCompensatingSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(CompensatingSaga).FullName);

    ISagaLifecycleProbe GetThrowingTimeoutSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(ThrowingTimeoutSaga).FullName);
}
