using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Conformance Shape B: the durable <c>OutboxEntry.ConversationId</c> field is
/// load-bearing. A staged effect whose forensic body cannot be JSON-rendered
/// dead-letters through the safety net's serialization-failure arm — the one
/// promote path that never deserializes the failing body — so the promoted row's
/// conversation id can only have come from the durable entry field. The scenario
/// stamps a known conversation id on the staged entry, reactivates the grain so the
/// entry makes a real grain-state round-trip before it ever drains, drives the real
/// drain to promotion, and asserts the id is still on the dead-letter row read back
/// from the real backend. It fails if the field is removed or stops being stamped on
/// the promote path. Bound against the degrade-arm fixture whose silo fails the
/// unserialisable-body publish at <c>OutboxMaxAttempts</c> = 2 with zero backoff.
/// </summary>
public abstract class ConversationIdSurvivesUndeserializableDeadLetterScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected ConversationIdSurvivesUndeserializableDeadLetterScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UndeserializableBody_DeadLetters_AndKeepsConversationIdAcrossRoundTrip()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);
        await probe.StageUnserialisableForensicBodyEntryWithConversationIdAsync(conversationId);

        // Reactivate so the staged entry is re-read from real grain storage before it
        // ever drains — the conversation id must survive the grain-state round-trip,
        // not merely live in memory.
        var activationBeforeDeactivation = await probe.GetActivationIdAsync();
        await probe.DeactivateAsync();
        await ConformanceWaiters.WaitUntilAsync(
            async () => await probe.GetActivationIdAsync() != activationBeforeDeactivation);

        // Act — drive the real drain until the failing entry exhausts its attempts and
        // the promoter degrades to a synthetic row. Zero backoff makes the entry
        // re-ready each tick, so the loop converges without a wall-clock gate.
        for (var tick = 0; tick < 12 && await probe.GetPendingOutboxCountAsync() > 0; tick++)
        {
            await probe.ForceDrainViaReminderAsync();
        }

        // Assert
        var deadLetterTable = _fixture.GetTableStore<EdictDeadLetterEntry>(EdictDeadLetterTable.Name);
        var row = await PromoterDegradeArmWaiters.WaitForDeadLetterRowAsync(
            deadLetterTable, counterId.ToString("N"));

        // The serialization-failure arm never reads the failing body, so the durable
        // OutboxEntry.ConversationId field is the only possible source of this id.
        Assert.Equal(nameof(EdictPromotionSerializationException), row.ExceptionType);
        Assert.Equal(conversationId, row.ConversationId);
    }
}
