using System.Diagnostics;

using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;
using Edict.Core.Audit;
using Edict.Core.Tests.Audit;

using Orleans;

using Xunit;

namespace Edict.Core.Tests.Schedules;

// Drives the hand-written schedule fire lifecycle through the ScheduleProbe under
// a virtual clock: a one-hour cadence keeps the real Orleans grain timer (armed
// for an hour of wall-clock) from firing during a seconds-long test, so the only
// fires are the ones the test drives by advancing the clock and calling the
// deterministic FireDueSchedulesAsync seam.
[Collection(ScheduleLifecycleCollection.Name)]
public sealed class ScheduleLifecycleTests
{
    static readonly TimeSpan Cadence = TimeSpan.FromHours(1);

    readonly ScheduleLifecycleClusterFixture _fixture;

    public ScheduleLifecycleTests(ScheduleLifecycleClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Fire_RaisingNoEvent_ShouldPersistStateMutation_AcrossForcedDeactivation()
    {
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(Guid.NewGuid().ToString("N"));
        await probe.StartAsync(Cadence, ScheduleProbeBehavior.ContinueNoEvent);

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();
        Assert.Equal(1, await probe.GetCounterAsync());

        // The no-event fire's mutation must survive the reload from durable storage.
        var activationBeforeDeactivation = await probe.GetActivationIdAsync();
        await probe.DeactivateAsync();
        await WaitForReactivationAsync(probe, activationBeforeDeactivation);

        Assert.Equal(1, await probe.GetCounterAsync());
    }

    [Fact]
    public async Task Fire_Continue_ShouldReArmAndFireAgainOnTheNextCadence()
    {
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(Guid.NewGuid().ToString("N"));
        await probe.StartAsync(Cadence, ScheduleProbeBehavior.ContinueNoEvent);

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();
        Assert.Equal(1, await probe.GetCounterAsync());

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();
        Assert.Equal(2, await probe.GetCounterAsync());

        Assert.NotNull(await probe.PeekSoonestScheduleDueAsync());
    }

    [Fact]
    public async Task Fire_Complete_ShouldStopTheSchedule()
    {
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(Guid.NewGuid().ToString("N"));
        await probe.StartAsync(Cadence, ScheduleProbeBehavior.CompleteNoEvent);

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();
        Assert.Equal(1, await probe.GetCounterAsync());
        Assert.Null(await probe.PeekSoonestScheduleDueAsync());

        // A further advance + fire finds nothing due — the schedule stopped.
        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();
        Assert.Equal(1, await probe.GetCounterAsync());
    }

    [Fact]
    public async Task Fire_AfterManyElapsedPeriods_ShouldCoalesceIntoASingleFire()
    {
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(Guid.NewGuid().ToString("N"));
        await probe.StartAsync(Cadence, ScheduleProbeBehavior.ContinueNoEvent);

        // Five cadences elapse while nothing fires; the catch-up must be one fire,
        // not a stampede of five.
        _fixture.AdvanceClock(TimeSpan.FromTicks(Cadence.Ticks * 5));
        await probe.FireDueSchedulesAsync();

        Assert.Equal(1, await probe.GetCounterAsync());
    }

    [Fact]
    public async Task Fire_RaisingEvent_ShouldStageAndDrainItThroughTheOutbox()
    {
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(Guid.NewGuid().ToString("N"));
        await probe.StartAsync(Cadence, ScheduleProbeBehavior.ContinueRaiseEvent);

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();

        // The fire ran and its raised event drained out of the outbox in the same
        // turn, leaving nothing stuck pending. End-to-end publication onto the
        // Timeline is proven by the consumer EdictTestApp test, where the
        // in-process dispatcher does not stall under the virtual clock.
        Assert.Equal(1, await probe.GetCounterAsync());
        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
    }

    [Fact]
    public async Task Fire_RaisingEvent_ShouldStampAFreshCorrelation_AsANewCausalRoot()
    {
        var probeId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(probeId.ToString("N"));
        await probe.StartAsync(Cadence, ScheduleProbeBehavior.ContinueRaiseEvent);

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();

        // A fire has no upstream message, so its raised event starts a fresh
        // correlation via the parameterless commit's mint — not the empty one the
        // arming command carried. Filter by this probe's key: the capturing
        // executor's queue is shared across the (serial) schedule tests.
        var published = Assert.Single(
            ScheduleRaiseCapturingExecutor.Captured.OfType<ScheduleTickedEvent>(),
            tick => tick.Key == probeId);
        Assert.NotEqual(Guid.Empty, published.CorrelationId);
    }

    [Fact]
    public async Task Fire_RaisingEvent_ShouldCarryTheArmingPrincipal()
    {
        var probeId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(probeId.ToString("N"));
        await probe.StartWithPrincipalAsync(Cadence, EdictPrincipal.Of("scheduler-bob"));

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();

        // A fire has no inbound message, so the only source of the actor is the
        // principal persisted in the schedule's arm-context: the recurring job stays
        // attributed to whoever armed it. Filter by this probe's key — the capturing
        // executor's queue is shared across the (serial) schedule tests.
        var published = Assert.Single(
            ScheduleRaiseCapturingExecutor.Captured.OfType<ScheduleTickedEvent>(),
            tick => tick.Key == probeId);
        Assert.Equal(EdictPrincipal.Of("scheduler-bob"), published.Principal);
    }

    [Fact]
    public async Task Fire_RaisingEvent_ShouldCarryTheArmingTenant()
    {
        var probeId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(probeId.ToString("N"));
        await probe.StartWithTenantAsync(Cadence, EdictTenantId.Of("acme"));

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();

        // A fire has no inbound message, so the only source of the tenant is the
        // tenant persisted in the schedule's arm-context: the recurring job stays
        // inside the wall it was armed under. Filter by this probe's key — the
        // capturing executor's queue is shared across the (serial) schedule tests.
        var published = Assert.Single(
            ScheduleRaiseCapturingExecutor.Captured.OfType<ScheduleTickedEvent>(),
            tick => tick.Key == probeId);
        Assert.Equal(EdictTenantId.Of("acme"), published.Tenant);
    }

    [Fact]
    public async Task Fire_RaisingEvent_ShouldCaptureAnE1RecordAttributedToTheArmingPrincipal()
    {
        var probeId = Guid.NewGuid();
        var entityKey = probeId.ToString("N");
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(probeId.ToString("N"));
        await probe.StartWithPrincipalAsync(Cadence, EdictPrincipal.Of("scheduler-bob"));

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();

        // A schedule fire raises an event through the same E1 chokepoint a command
        // does, so it is captured and drained off the fire turn even though no
        // command ran it. The record is attributed to the durable arm-context
        // principal, not an ambient one.
        var records = await WaitForEntityRecordsAsync(entityKey, expectedCount: 1);
        var eventRecord = Assert.Single(records, record => record.Kind == EdictAuditKind.Event);
        Assert.Equal(EdictPrincipal.Of("scheduler-bob"), eventRecord.Principal);
        Assert.Equal(typeof(ScheduleTickedEvent).FullName, eventRecord.MessageType);
        Assert.Null(eventRecord.Outcome);

        var verification = await new EdictDefaultAuditRepository(_fixture.AuditStore, new RecordingAuditPayloadStore(), _fixture.Serializer)
            .VerifyEntityChainAsync(typeof(ScheduleProbe).FullName!, entityKey);
        Assert.True(verification.IsIntact);
    }

    async Task<IReadOnlyList<EdictAuditRecord>> WaitForEntityRecordsAsync(string entityKey, int expectedCount)
    {
        var entityType = typeof(ScheduleProbe).FullName!;
        var deadline = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * 10);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var records = await _fixture.AuditStore.ByEntityAsync(entityType, entityKey, tenant: null, CancellationToken.None);
            if (records.Count(record => record.Kind == EdictAuditKind.Event) >= expectedCount)
            {
                return records;
            }
            await Task.Delay(25);
        }

        Assert.Fail($"Expected {expectedCount} event audit record(s) for {entityKey} but the drain did not produce them in time.");
        return [];
    }

    [Fact]
    public async Task Fire_AfterReactivation_ShouldStillCarryTheArmingPrincipal()
    {
        var probeId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(probeId.ToString("N"));
        await probe.StartWithPrincipalAsync(Cadence, EdictPrincipal.Of("scheduler-bob"));

        // Force a reactivation between arming and firing: the arm-context principal
        // survives only because it is durable schedule state, not ambient context.
        var activationBeforeDeactivation = await probe.GetActivationIdAsync();
        await probe.DeactivateAsync();
        await WaitForReactivationAsync(probe, activationBeforeDeactivation);

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();

        var published = Assert.Single(
            ScheduleRaiseCapturingExecutor.Captured.OfType<ScheduleTickedEvent>(),
            tick => tick.Key == probeId);
        Assert.Equal(EdictPrincipal.Of("scheduler-bob"), published.Principal);
    }

    [Fact]
    public async Task Fire_ThatThrows_ShouldRollBackStateMutation_AndKeepTheScheduleArmed()
    {
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(Guid.NewGuid().ToString("N"));
        await probe.StartAsync(Cadence, ScheduleProbeBehavior.Throw);

        _fixture.AdvanceClock(Cadence);
        // The fire handler increments State then throws; the lifecycle catches it,
        // so the fire seam itself does not surface the exception.
        await probe.FireDueSchedulesAsync();

        // The partial +1 was rolled back to the last durable snapshot.
        Assert.Equal(0, await probe.GetCounterAsync());
        // The schedule re-timed forward rather than dead-lettering (no timeout path yet).
        Assert.NotNull(await probe.PeekSoonestScheduleDueAsync());
    }

    [Fact]
    public async Task Timeout_WithCompensationHook_ShouldRunItAndStopTheSchedule()
    {
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(Guid.NewGuid().ToString("N"));
        // Cap shorter than one cadence so a single timeout fire precedes any tick.
        await probe.StartWithTimeoutAsync(Cadence, TimeSpan.FromMinutes(1), ScheduleProbeTimeoutBehavior.Compensate);

        _fixture.AdvanceClock(TimeSpan.FromMinutes(1));
        await probe.FireDueScheduleTimeoutsAsync();

        Assert.Equal(1, await probe.GetTimeoutCounterAsync());
        // The tick handler never ran, and the schedule is gone (timeout is terminal).
        Assert.Equal(0, await probe.GetCounterAsync());
        Assert.Null(await probe.PeekSoonestScheduleTimeoutAsync());
        Assert.Null(await probe.PeekSoonestScheduleDueAsync());
    }

    [Fact]
    public async Task Timeout_WithNoHook_ShouldDeadLetter_StopTheSchedule_AndNotThrow()
    {
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(Guid.NewGuid().ToString("N"));
        await probe.StartWithTimeoutAsync(Cadence, TimeSpan.FromMinutes(1), ScheduleProbeTimeoutBehavior.DeadLetter);

        _fixture.AdvanceClock(TimeSpan.FromMinutes(1));
        // The unhandled cap dead-letters through DeadLetterPromoter without throwing.
        await probe.FireDueScheduleTimeoutsAsync();

        Assert.Equal(0, await probe.GetTimeoutCounterAsync());
        Assert.Null(await probe.PeekSoonestScheduleTimeoutAsync());
        Assert.Null(await probe.PeekSoonestScheduleDueAsync());
        // The dead-letter publish drained out of the outbox in the same turn.
        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
    }

    [Fact]
    public async Task Timeout_Cap_ShouldBeArmedOnce_AndNotPushedByTicks()
    {
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(Guid.NewGuid().ToString("N"));
        // Cadence one minute, cap two minutes: a tick fires at +1m, then the cap
        // fires at +2m even though the tick advanced the schedule's next due.
        await probe.StartWithTimeoutAsync(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), ScheduleProbeTimeoutBehavior.Compensate);

        _fixture.AdvanceClock(TimeSpan.FromMinutes(1));
        await probe.FireDueSchedulesAsync();
        Assert.Equal(1, await probe.GetCounterAsync());

        _fixture.AdvanceClock(TimeSpan.FromMinutes(1));
        await probe.FireDueScheduleTimeoutsAsync();

        // The cap fired on schedule despite the intervening tick re-arming the due.
        Assert.Equal(1, await probe.GetTimeoutCounterAsync());
        Assert.Null(await probe.PeekSoonestScheduleDueAsync());
    }

    static Task WaitForReactivationAsync(IScheduleProbe probe, Guid previousActivationId) =>
        WaitUntilAsync(async () => await probe.GetActivationIdAsync() != previousActivationId);

    static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
