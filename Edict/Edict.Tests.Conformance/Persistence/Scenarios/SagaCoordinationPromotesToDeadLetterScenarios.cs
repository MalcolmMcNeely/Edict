using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Verifies the dead-letter row carries the <see cref="EdictSagaCoordinationException"/>
/// type name when a saga's one-command-per-event coordination is violated. The
/// controllable executor injects the throw at the outbox effect boundary because
/// the saga handler path itself escapes via the stream subsystem and not the
/// dead-letter pipeline — the path under test is the catch in
/// <c>OutboxHost.ExecuteGroupCapturingAsync</c> and the row the dead-letter
/// projection writes.
/// </summary>
public abstract class SagaCoordinationPromotesToDeadLetterScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected SagaCoordinationPromotesToDeadLetterScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Promotes_ShouldNameTypedExceptionOnRow()
    {
        var counterId = Guid.NewGuid();
        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.FailureKind = ControllableFailureKind.SagaCoordination;
        _fixture.OutboxFault.ShouldFail = true;

        await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            await probe.ForceDrainViaReminderAsync();
            return _fixture.OutboxFault.FailedAttempts >= 2;
        });

        _fixture.OutboxFault.ShouldFail = false;

        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            await probe.ForceDrainViaReminderAsync();
            return await probe.GetPendingOutboxCountAsync() == 0;
        });

        var deadLetterTable = _fixture.GetTableStore<EdictDeadLetterEntry>(
            EdictDeadLetterTable.Name);

        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            var entries = await deadLetterTable.QueryPartitionAsync(
                EdictDeadLetterTable.Name);
            return entries.Any(e => e.SourceGrainKey.Contains(counterId.ToString("N")));
        });

        var allEntries = await deadLetterTable.QueryPartitionAsync(
            EdictDeadLetterTable.Name);
        var entry = allEntries.Single(e => e.SourceGrainKey.Contains(counterId.ToString("N")));

        Assert.Equal(typeof(EdictSagaCoordinationException).FullName, entry.ExceptionType);
    }
}
