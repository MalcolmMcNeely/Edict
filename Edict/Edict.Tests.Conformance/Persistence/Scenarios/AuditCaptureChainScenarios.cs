using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Persistence-axis conformance for audit capture: an accepted-and-raising command
/// and a rejected command under a known principal yield immutable, attributed
/// records on one unbroken per-aggregate chain. The accepted increment captures one
/// C1 command record plus one E1 event record; the rejection captures one C1 command
/// record and raises nothing. Every record is attributed to the known principal, the
/// kinds and sequence are exactly as captured, the rejection carries its reason, and
/// the chain verifies.
/// </summary>
public abstract class AuditCaptureChainScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IAuditConformanceFixture
{
    readonly TFixture _fixture;

    protected AuditCaptureChainScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    static string EntityType => typeof(CounterAggregate).FullName!;

    [Fact]
    public async Task AcceptRaiseAndReject_UnderAKnownPrincipal_YieldImmutableChainedCommandAndEventRecords()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString();

        // Act — the accepted increment raises one event (one C1 command record plus
        // one E1 event record); the rejection raises nothing (one C1 command record).
        var accepted = await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        var rejected = await _fixture.Sender.SendAsync(new RejectCounterCommand(counterId));

        // Assert — every record attributed to the known principal, on one chain.
        var cursor = Assert.IsType<EdictCommandResult.Accepted>(accepted).Cursor;
        Assert.IsType<EdictCommandResult.Rejected>(rejected);

        var records = await AuditConformanceWaiters.WaitForEntityRecordsAsync(_fixture, EntityType, entityKey, expectedCount: 3);
        Assert.All(records, record => Assert.Equal(_fixture.AuditPrincipal, record.Principal));
        Assert.Equal(
            [EdictAuditKind.Command, EdictAuditKind.Event, EdictAuditKind.Command],
            records.Select(record => record.Kind));
        Assert.Equal([0L, 1L, 2L], records.Select(record => record.Sequence));

        // The command decisions: accepted then rejected, with reasons on the rejection.
        var commandRecords = records.Where(record => record.Kind == EdictAuditKind.Command).ToArray();
        Assert.Equal(EdictAuditOutcome.Accepted, commandRecords[0].Outcome);
        Assert.Equal(EdictAuditOutcome.Rejected, commandRecords[1].Outcome);
        Assert.Equal("counter_rejected", Assert.Single(commandRecords[1].RejectionReasons).Code);

        // The E1 event record: present, attributed, carrying the inherited
        // correlation, with no command outcome.
        var eventRecord = Assert.Single(records, record => record.Kind == EdictAuditKind.Event);
        Assert.Equal(cursor.CorrelationId, eventRecord.CorrelationId);
        Assert.Null(eventRecord.Outcome);
        Assert.Equal(typeof(CounterIncrementedEvent).FullName, eventRecord.MessageType);

        var verification = await _fixture.VerifyChainAsync(EntityType, entityKey);
        Assert.True(verification.IsIntact);
    }
}
