using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Tests.Conformance.Sagas;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Substrate-agnostic conformance for the saga absolute-lifetime cap firing on a
/// saga that declares <em>no</em> <c>OnSagaTimeoutAsync</c> override: the base's
/// default hook routes the fired cap to dead-letter, so a forgotten timeout
/// surfaces loudly rather than silently stranding the workflow. The cap is driven
/// deterministically on the fixture's virtual clock — advance past the one-second
/// cap, fire it through the probe — and the assertion observes the durable,
/// externally-visible outcome on a real backend: a single
/// <see cref="EdictSagaTimeoutException"/> dead-letter row. The
/// <c>EdictSagaTimeoutException</c> type distinguishes this by-design no-override
/// path from the faulting-compensation path (an
/// <see cref="EdictSagaCompensationException"/> row) and the terminal-guard path
/// (an <see cref="EdictSagaTerminalException"/> row).
/// </summary>
public abstract class SagaTimeoutDefaultHookDeadLetterScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    const string Stream = "ConformanceSagaTimeoutDefaultHookWorkflow";

    readonly TFixture _fixture;

    protected SagaTimeoutDefaultHookDeadLetterScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FiredCapWithNoOverride_ShouldDeadLetterViaDefaultHook()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<ISagaTimeoutPublisher>(workflowId);

        await publisher.PublishAsync(Stream, new TimeoutDefaultHookTriggerEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        // The reference stream's pulling agent polls on the silo's virtual clock, so
        // pump the clock until the trigger is delivered and handled — that handle
        // arms the absolute cap.
        var saga = _fixture.GrainFactory.GetGrain<IDefaultHookTimeoutSagaProbe>(workflowId);
        await SagaTimeoutWaiters.PumpUntilAsync(_fixture.AdvanceClock, async () => await saga.GetHandledAsync() >= 1);

        // A saga is activated by implicit stream subscription, so its grain key is the
        // stream's Guid — matched format-agnostically since a Guid key can surface in
        // either the dashless or canonical form.
        bool IsForThisWorkflow(EdictDeadLetterEntry entry) =>
            entry.SourceGrainKey.Contains(workflowId.ToString("N"))
            || entry.SourceGrainKey.Contains(workflowId.ToString());

        // Act — push past the one-second cap on the virtual clock, then fire it.
        _fixture.AdvanceClock(TimeSpan.FromSeconds(2));
        await saga.FireCapAsync();

        // The default hook's dead-letter publishes to the dead-letter stream; pump the
        // clock so its pulling agent delivers it to the projection, which upserts the
        // row on the real backend.
        var deadLetterTable = _fixture.GetTableStore<EdictDeadLetterEntry>(EdictDeadLetterTable.Name);
        await SagaTimeoutWaiters.PumpUntilAsync(_fixture.AdvanceClock, async () =>
        {
            var entries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
            return entries.Any(IsForThisWorkflow);
        });

        // Assert
        var allEntries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
        var workflowDeadLetters = allEntries.Where(IsForThisWorkflow).ToArray();

        Assert.Single(workflowDeadLetters);
        Assert.Equal(typeof(EdictSagaTimeoutException).FullName, workflowDeadLetters[0].ExceptionType);
    }
}
