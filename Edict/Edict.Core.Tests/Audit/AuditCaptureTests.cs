using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;

using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Core.Audit;
using Edict.Core.Tests.Grains;

using Xunit;

namespace Edict.Core.Tests.Audit;

// Drives accepted and rejected commands through the real engine under auditing,
// then reads the captured, drained records the way a consumer would — the record
// in the store, the chain a verifier reports — never an internal field.
[Collection(AuditCaptureCollection.Name)]
public sealed class AuditCaptureTests(AuditCaptureClusterFixture fixture)
{
    static string EntityType => typeof(CounterAggregate).FullName!;

    [Fact]
    public async Task Capture_ShouldRecordOneAttributedRecordPerDecision_WithAnUnbrokenChain()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString();

        // Act
        var accepted = await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        var rejected = await fixture.Sender.SendAsync(new RejectByValidatorCommand(counterId));

        // Assert
        Assert.IsType<EdictCommandResult.Accepted>(accepted);
        Assert.IsType<EdictCommandResult.Rejected>(rejected);

        // The accepted increment captures one C1 command record plus one E1 event
        // record (it raises one event); the validator rejection captures one C1
        // command record and raises nothing — three records on one chain.
        var records = await WaitForRecordsAsync(entityKey, expectedCount: 3);

        Assert.All(records, record => Assert.Equal(AuditCaptureClusterFixture.CapturePrincipal, record.Principal));
        Assert.Equal([0L, 1L, 2L], records.Select(record => record.Sequence));

        var commandRecords = records.Where(record => record.Kind == EdictAuditKind.Command).ToArray();
        Assert.Equal(EdictAuditOutcome.Accepted, commandRecords[0].Outcome);
        Assert.Equal(EdictAuditOutcome.Rejected, commandRecords[1].Outcome);
        Assert.Equal("always_rejected", Assert.Single(commandRecords[1].RejectionReasons).Code);

        var repository = new EdictDefaultAuditRepository(fixture.AuditStore, fixture.PayloadStore);
        var verification = await repository.VerifyEntityChainAsync(EntityType, entityKey);
        Assert.True(verification.IsIntact);
        Assert.Null(verification.BrokenAtSequence);
    }

    [Fact]
    public async Task Capture_ShouldRecordOneEventRecordPerRaisedEvent_ChainedAfterTheCommand()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString();

        // Act
        var accepted = await fixture.Sender.SendAsync(new BatchIncrementCounterCommand(counterId, 2));

        // Assert
        var cursor = Assert.IsType<EdictCommandResult.Accepted>(accepted).Cursor;

        var records = await WaitForRecordsAsync(entityKey, expectedCount: 3);

        // One C1 command record, then one E1 event record per raised event, all on
        // one unbroken chain attributed to the carried principal.
        Assert.Equal(
            [EdictAuditKind.Command, EdictAuditKind.Event, EdictAuditKind.Event],
            records.Select(record => record.Kind));
        Assert.Equal([0L, 1L, 2L], records.Select(record => record.Sequence));

        var eventRecords = records.Where(record => record.Kind == EdictAuditKind.Event).ToArray();
        Assert.All(eventRecords, record =>
        {
            Assert.Equal(AuditCaptureClusterFixture.CapturePrincipal, record.Principal);
            Assert.Equal(cursor.CorrelationId, record.CorrelationId);
            Assert.Null(record.Outcome);
            Assert.Equal(typeof(CounterIncrementedEvent).FullName, record.MessageType);
        });

        var repository = new EdictDefaultAuditRepository(fixture.AuditStore, fixture.PayloadStore);
        var verification = await repository.VerifyEntityChainAsync(EntityType, entityKey);
        Assert.True(verification.IsIntact);
        Assert.Null(verification.BrokenAtSequence);
    }

    [Fact]
    public async Task Capture_ShouldEmitTheRecordsCapturedCounter_TaggedByKindAndOutcome()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var captured = new List<(string Kind, string Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "edict.audit.records.captured")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            string kind = "", outcome = "";
            foreach (var tag in tags)
            {
                if (tag.Key == "edict.audit.kind")
                {
                    kind = (string)tag.Value!;
                }
                if (tag.Key == "edict.audit.outcome")
                {
                    outcome = (string)tag.Value!;
                }
            }
            lock (captured)
            {
                captured.Add((kind, outcome));
            }
        });
        listener.Start();

        // Act — the accepted command raises one event, so it captures one C1
        // command record plus one E1 event record; the validator rejection captures
        // one C1 command record and raises nothing.
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        await fixture.Sender.SendAsync(new RejectByValidatorCommand(counterId));
        await WaitForRecordsAsync(counterId.ToString(), expectedCount: 3);

        // Assert
        lock (captured)
        {
            Assert.Contains(("command", "accepted"), captured);
            Assert.Contains(("command", "rejected"), captured);
            Assert.Contains(("event", ""), captured);
        }
    }

    [Fact]
    public async Task GetPayload_ShouldReturnTheCapturedCommandBody_WithAHashMatchingTheRecord()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString();

        // Act
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        var records = await WaitForRecordsAsync(entityKey, expectedCount: 2);

        // Assert — the C1 command record's body is retrievable, and its bytes hash
        // to exactly the PayloadHash sealed into the chain.
        var commandRecord = Assert.Single(records, record => record.Kind == EdictAuditKind.Command);
        var repository = new EdictDefaultAuditRepository(fixture.AuditStore, fixture.PayloadStore);

        var body = await repository.GetPayloadAsync(commandRecord.RecordId);

        Assert.NotEmpty(body.ToArray());
        Assert.Equal(commandRecord.PayloadHash, SHA256.HashData(body.Span));
    }

    [Fact]
    public async Task GetPayload_ShouldReturnEachCapturedEventBody_WithAHashMatchingTheRecord()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString();

        // Act — a batch increment raises two events, so two E1 records each with a body.
        await fixture.Sender.SendAsync(new BatchIncrementCounterCommand(counterId, 2));
        var records = await WaitForRecordsAsync(entityKey, expectedCount: 3);

        // Assert — every E1 event body is retrievable and hashes to its record.
        var repository = new EdictDefaultAuditRepository(fixture.AuditStore, fixture.PayloadStore);
        var eventRecords = records.Where(record => record.Kind == EdictAuditKind.Event).ToArray();
        Assert.Equal(2, eventRecords.Length);
        foreach (var eventRecord in eventRecords)
        {
            var body = await repository.GetPayloadAsync(eventRecord.RecordId);
            Assert.NotEmpty(body.ToArray());
            Assert.Equal(eventRecord.PayloadHash, SHA256.HashData(body.Span));
        }
    }

    async Task<IReadOnlyList<EdictAuditRecord>> WaitForRecordsAsync(string entityKey, int expectedCount)
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var records = await fixture.AuditStore.ByEntityAsync(EntityType, entityKey, CancellationToken.None);
            if (records.Count >= expectedCount)
            {
                return records;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Expected {expectedCount} audit records for {entityKey} but the drain did not produce them in time.");
        return [];
    }
}
