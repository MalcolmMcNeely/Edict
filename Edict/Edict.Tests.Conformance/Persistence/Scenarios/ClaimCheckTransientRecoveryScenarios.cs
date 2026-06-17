using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Tests.Conformance.ClaimCheck;

using Orleans.Serialization;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// A claim-check fetch that fails transiently then succeeds must <em>deliver</em>,
/// not dead-letter — the path that justifies running the fetch inside a retryable
/// outbox entry. A pointer envelope is staged with its body already present in the
/// store, the controllable store holds the fetch down for a count-addressed number
/// of attempts, then heals; the engine's per-entry retry fetches and dispatches the
/// inner event. The asserted outcome is the mirror image of the permanent-missing
/// path (<see cref="MissingClaimCheckDeadLetterClassificationScenarios{TFixture}"/>):
/// the event is handled exactly once and <strong>no</strong> dead-letter row lands.
/// Recovery is driven by the count-addressed fault plus reminder-drain ticks — a
/// tick's grain round-trip outlasts the fixture's minimal backoff, so there is no
/// wall-clock gate.
/// </summary>
public abstract class ClaimCheckTransientRecoveryScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IClaimCheckStoreFixture
{
    readonly TFixture _fixture;

    protected ClaimCheckTransientRecoveryScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TransientFetchFailures_ShouldRecoverAndDeliver_WithoutDeadLettering()
    {
        // Arrange — park a body in the store under the pointer envelope's id, then
        // hold the fetch down for two attempts. The body is present the whole time,
        // so this is a transient fetch fault, not a permanent miss.
        _fixture.ClaimCheckFault.Reset();
        var grainId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var consumer = _fixture.GrainFactory.GetGrain<IClaimCheckTransientRecoveryConsumer>(grainId);

        var innerEvent = new ClaimCheckCounterIncrementedEvent(Guid.NewGuid(), 1, "recovered") { EventId = eventId };
        var body = _fixture.Serializer.SerializeToArray<EdictEvent>(innerEvent);
        await _fixture.PutClaimCheckAsync(tenant: null, eventId, body, CancellationToken.None);

        _fixture.ClaimCheckFault.FailFetchUntilAttempt = 2;

        var envelope = new EdictEventEnvelope(inlinePayload: null, eventId: eventId)
        {
            OccurredAt = DateTimeOffset.UtcNow,
            InnerEventStreamName = "ConformanceClaimCheckTransientRecovery",
            InnerEventRouteKey = grainId.ToString("N"),
        };
        await consumer.DeliverAsync(envelope);

        // Act — drive the drain. The first two fetch attempts fault, then the
        // count-addressed fault heals and a later tick fetches and dispatches.
        for (var tick = 0; tick < 15 && await consumer.GetHandledCountAsync() == 0; tick++)
        {
            await consumer.ForceDrainViaReminderAsync();
        }

        // Assert — the fetch faulted twice, the event delivered exactly once, and
        // no dead-letter row was promoted for it.
        Assert.True(_fixture.ClaimCheckFault.FailedFetches >= 2);
        Assert.Equal(1, await consumer.GetHandledCountAsync());

        var deadLetterTable = _fixture.GetTableStore<EdictDeadLetterEntry>(EdictDeadLetterTable.Name);
        var entries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
        Assert.DoesNotContain(entries, entry => entry.SourceEventId == eventId);
    }
}
