using System.Diagnostics;
using System.Diagnostics.Metrics;

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

        var records = await WaitForRecordsAsync(entityKey, expectedCount: 2);

        Assert.All(records, record => Assert.Equal(AuditCaptureClusterFixture.CapturePrincipal, record.Principal));
        Assert.Equal(EdictAuditOutcome.Accepted, records[0].Outcome);
        Assert.Equal(EdictAuditOutcome.Rejected, records[1].Outcome);
        Assert.Equal("always_rejected", Assert.Single(records[1].RejectionReasons).Code);
        Assert.Equal([0L, 1L], records.Select(record => record.Sequence));

        var repository = new EdictDefaultAuditRepository(fixture.AuditStore);
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

        // Act
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        await fixture.Sender.SendAsync(new RejectByValidatorCommand(counterId));
        await WaitForRecordsAsync(counterId.ToString(), expectedCount: 2);

        // Assert
        lock (captured)
        {
            Assert.Contains(("command", "accepted"), captured);
            Assert.Contains(("command", "rejected"), captured);
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
