using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Core.Tests.Grains;

namespace Edict.Core.Tests.DeadLetter;

// Minimal integration coverage of the built-in singleton table projection
// (ADR 0022). The grain itself is one Handle overload plus GetRowKey/table-name
// overrides; deeper assertions belong on the base. This test proves the wiring
// — the EdictDeadLetterRaised reaches the grain via the "edict-dead-letter"
// implicit subscription, the Handle maps every RCA field to a row, and the row
// lands under the fixed "deadletter" partition keyed by EntryId.
[Collection(EdictClusterCollection.Name)]
public sealed class EdictDeadLetterProjectionBuilderTests(EdictClusterFixture fixture)
{
    [Fact]
    public async Task HandleAsync_ShouldUpsertRowKeyedByEntryId_WhenEdictDeadLetterRaisedDelivered()
    {
        var entryId = Guid.NewGuid();
        var raised = new EdictDeadLetterRaised
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            EntryId = entryId,
            Kind = "PublishEvent",
            AttemptCount = 8,
            DeadLetteredAt = DateTimeOffset.UtcNow,
            SourceGrainKey = "33333333-3333-3333-3333-333333333333",
            SourceGrainType = "Sample.OrderCommandHandler",
            EffectTarget = "Orders/OrderPlacedEvent",
            TraceParent = null,
            ExceptionType = "System.InvalidOperationException",
            Reason = "downstream unavailable",
            PayloadJson = "{\"OrderId\":\"22222222-2222-2222-2222-222222222222\"}",
        };

        var publisher = fixture.Cluster.GrainFactory
            .GetGrain<IProjectionPublisherGrain>(EdictDeadLetterRaised.SingletonGrainKey);
        await publisher.PublishToStreamAsync("edict-dead-letter", raised);

        await WaitForRowAsync(entryId);

        var store = fixture.TableStoreFactory.GetStore<EdictDeadLetterEntry>(
            EdictDeadLetterProjectionBuilder.DeadLetterPartition);
        var row = store.Get(
            EdictDeadLetterProjectionBuilder.DeadLetterPartition, entryId.ToString("N"));

        await Verify(row).DontScrubGuids().DontScrubDateTimes()
            .ScrubMember<EdictDeadLetterEntry>(e => e.EntryId)
            .ScrubMember<EdictDeadLetterEntry>(e => e.DeadLetteredAt);
    }

    async Task WaitForRowAsync(Guid entryId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var store = fixture.TableStoreFactory.GetStore<EdictDeadLetterEntry>(
                    EdictDeadLetterProjectionBuilder.DeadLetterPartition);
                var row = store.Get(
                    EdictDeadLetterProjectionBuilder.DeadLetterPartition, entryId.ToString("N"));
                if (row is not null)
                {
                    return;
                }
            }
            catch (KeyNotFoundException) { }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
