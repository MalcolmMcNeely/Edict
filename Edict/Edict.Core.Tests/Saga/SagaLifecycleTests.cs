using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Core.Sagas;

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

    ISagaLifecycleProbe GetDefaultCapSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(DefaultCapSaga).FullName);

    ISagaLifecycleProbe GetCappedSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(CappedSaga).FullName);

    ISagaLifecycleProbe GetCompensatingSaga(Guid workflowId) =>
        _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(CompensatingSaga).FullName);
}
