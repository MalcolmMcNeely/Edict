using System.Diagnostics.Metrics;

using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Telemetry;
using Edict.Tests.Conformance.Sagas;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Substrate-agnostic conformance for the saga absolute-lifetime cap converging
/// when the consumer's <c>OnSagaTimeoutAsync</c> override throws — the poison-loop
/// the containment (catch → rollback → dead-letter as <c>ConsumerBug</c> + the
/// <c>compensation_failed</c> timeout outcome) was built to stop. The cap is driven
/// deterministically on the fixture's virtual clock: advance past the one-second
/// cap, fire it through the probe, and observe the durable, externally-visible
/// outcome on a real backend — a single <see cref="EdictSagaCompensationException"/>
/// dead-letter row, the <c>compensation_failed</c> outcome on
/// <c>edict.saga.timeout.fired</c>, and the outbox settling to zero. A second fire
/// against the now-terminal saga is a no-op, so the cap never re-throws and the
/// workflow converges within a bounded number of steps.
/// </summary>
public abstract class SagaTimeoutCompensationFailureScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    const string Stream = "ConformanceSagaTimeoutThrowingWorkflow";

    readonly TFixture _fixture;

    protected SagaTimeoutCompensationFailureScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ThrowingTimeoutHook_ShouldDeadLetterOnceAsConsumerBug_AndConverge()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<ISagaTimeoutPublisher>(workflowId);

        await publisher.PublishAsync(Stream, new TimeoutThrowingTriggerEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        // The reference stream's pulling agent polls on the silo's virtual clock, so
        // pump the clock until the trigger is delivered and handled — that handle
        // arms the absolute cap.
        var saga = _fixture.GrainFactory.GetGrain<IThrowingTimeoutSagaProbe>(workflowId);
        await SagaTimeoutWaiters.PumpUntilAsync(_fixture.AdvanceClock, async () => await saga.GetHandledAsync() >= 1);

        var outcomes = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.Sagas.Meters.TimeoutFired)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var captured = new Dictionary<string, object?>(tags.Length);
            foreach (var tag in tags)
            {
                captured[tag.Key] = tag.Value;
            }
            // Peer fixtures share the test process; filter to this saga type so a
            // peer's fired cap never contaminates the capture.
            if ((captured.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string)?
                    .Contains(nameof(TimeoutThrowingSaga)) == true)
            {
                lock (outcomes)
                {
                    outcomes.Add(captured.GetValueOrDefault(SemanticConventions.Sagas.Tags.Outcome) as string ?? "");
                }
            }
        });
        listener.Start();

        // The cap arms on that first handle.
        Assert.Equal(1, await saga.GetHandledAsync());

        // A saga is activated by implicit stream subscription, so its grain key is the
        // stream's Guid — matched format-agnostically since a Guid key can surface in
        // either the dashless or canonical form.
        bool IsForThisWorkflow(EdictDeadLetterEntry entry) =>
            entry.SourceGrainKey.Contains(workflowId.ToString("N"))
            || entry.SourceGrainKey.Contains(workflowId.ToString());

        // Act — push past the one-second cap on the virtual clock, then fire it.
        _fixture.AdvanceClock(TimeSpan.FromSeconds(2));
        await saga.FireCapAsync();

        // The contained throw publishes the dead-letter to the dead-letter stream;
        // pump the clock so its pulling agent delivers it to the projection, which
        // upserts the row on the real backend.
        var deadLetterTable = _fixture.GetTableStore<EdictDeadLetterEntry>(EdictDeadLetterTable.Name);
        await SagaTimeoutWaiters.PumpUntilAsync(_fixture.AdvanceClock, async () =>
        {
            var entries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
            return entries.Any(IsForThisWorkflow);
        });

        // The fired cap's dead-letter publish drains, so the saga outbox settles to zero.
        await SagaTimeoutWaiters.WaitUntilAsync(async () => await saga.GetPendingOutboxCountAsync() == 0);

        // Convergence: a second fire against the now-terminal saga is a no-op — it
        // neither re-throws, re-dead-letters, nor re-arms the cap.
        await saga.FireCapAsync();

        // Assert
        var allEntries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
        var workflowDeadLetters = allEntries.Where(IsForThisWorkflow).ToArray();

        Assert.Single(workflowDeadLetters);
        Assert.Equal(typeof(EdictSagaCompensationException).FullName, workflowDeadLetters[0].ExceptionType);
        Assert.Equal(0, await saga.GetPendingOutboxCountAsync());

        lock (outcomes)
        {
            Assert.Single(outcomes);
            Assert.Equal(SemanticConventions.Sagas.Tags.OutcomeValues.CompensationFailed, outcomes[0]);
        }
    }
}
