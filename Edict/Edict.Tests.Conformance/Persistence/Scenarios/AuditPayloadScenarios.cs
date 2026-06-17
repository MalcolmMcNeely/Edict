using System.Security.Cryptography;

using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Persistence-axis conformance for the audit payload store: every captured record's
/// body is retrievable by record id and hashes to the record's sealed
/// <see cref="Contracts.Audit.EdictAuditRecord.PayloadHash"/>, and that holds after
/// the capturing grain is forced to reactivate so the body is read from the durable
/// store rather than memory.
/// </summary>
public abstract class AuditPayloadScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IAuditConformanceFixture
{
    readonly TFixture _fixture;

    protected AuditPayloadScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    static string EntityType => typeof(CounterAggregate).FullName!;

    [Fact]
    public async Task Payload_ShouldBeRetrievableWithAMatchingHash_AndSurviveReactivation()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString("N");
        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        // The increment captures a C1 command record plus an E1 event record, each
        // with a body in the payload store.
        await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        var records = await AuditConformanceWaiters.WaitForEntityRecordsAsync(_fixture, EntityType, entityKey, expectedCount: 2);

        // Act — force a reactivation so the payload is read from the durable store,
        // not memory.
        var activationBeforeDeactivation = await probe.GetActivationIdAsync();
        await probe.DeactivateAsync();
        await AuditConformanceWaiters.WaitUntilAsync(async () => await probe.GetActivationIdAsync() != activationBeforeDeactivation);

        // Assert — every record's body is retrievable and hashes to the record's
        // sealed PayloadHash.
        foreach (var record in records)
        {
            var body = await _fixture.GetPayloadAsync(record.RecordId);
            Assert.NotEmpty(body.ToArray());
            Assert.Equal(record.PayloadHash, SHA256.HashData(body.Span));
        }
    }
}
