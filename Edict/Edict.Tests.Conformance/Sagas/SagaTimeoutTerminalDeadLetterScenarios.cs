using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;

using Xunit;

namespace Edict.Tests.Conformance.Sagas;

/// <summary>
/// Substrate-agnostic conformance for the saga terminal guard: a genuinely-new
/// Event arriving after the saga called <c>Complete()</c> dead-letters with the
/// <see cref="EdictSagaTerminalException"/> type on the row. The assertion
/// observes the durable dead-letter row the projection writes, not the saga's
/// internal state. The deferred fact proves the guard also fires when the new
/// Event arrives oversized (the claim-check pointer-envelope path): the body
/// materialises on the drain, the guard recognises it as a handled type, and it
/// dead-letters.
/// </summary>
public abstract class SagaTimeoutTerminalDeadLetterScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    const string Stream = "ConformanceSagaTerminalWorkflow";

    readonly TFixture _fixture;

    protected SagaTimeoutTerminalDeadLetterScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public Task NewEventAtTerminalSaga_ShouldDeadLetter_WhenItArrivesInline() =>
        NewEventDeadLettersAsync(oversized: false);

    [Fact]
    public Task NewEventAtTerminalSaga_ShouldDeadLetter_WhenItArrivesOversized() =>
        NewEventDeadLettersAsync(oversized: true);

    async Task NewEventDeadLettersAsync(bool oversized)
    {
        var workflowId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<ISagaTimeoutPublisher>(workflowId);

        await publisher.PublishAsync(Stream, new TerminalTriggerEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await publisher.PublishAsync(Stream, new TerminalFinishEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        // Wait until both Events are handled — the second calls Complete(), so the
        // saga is terminal before the genuinely-new Event below arrives.
        var saga = _fixture.GrainFactory.GetGrain<ITerminalSagaProbe>(workflowId);
        await SagaTimeoutWaiters.WaitUntilAsync(async () => await saga.GetHandledAsync() >= 2);

        var newEvent = new TerminalTriggerEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        if (oversized)
        {
            await publisher.PublishOversizedAsync(Stream, newEvent);
        }
        else
        {
            await publisher.PublishAsync(Stream, newEvent);
        }

        var deadLetterTable = _fixture.GetTableRepository<EdictDeadLetterEntry>(EdictDeadLetterTable.Name);
        await SagaTimeoutWaiters.WaitUntilAsync(async () =>
        {
            var entries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
            return entries.Any(entry => entry.SourceGrainKey.Contains(workflowId.ToString()));
        });

        var allEntries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
        var deadLetter = allEntries.Single(entry => entry.SourceGrainKey.Contains(workflowId.ToString()));

        Assert.Equal(typeof(EdictSagaTerminalException).FullName, deadLetter.ExceptionType);
    }
}
