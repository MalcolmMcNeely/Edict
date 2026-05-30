using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.DeadLetter;

/// <summary>
/// When the outbox executor exhausts its attempts on a publish, the
/// <c>EdictDeadLetterRaised</c> projection must land a row in the literal
/// <c>deadletter</c> table (independent of any per-fixture table-name aliasing
/// behind the operator-facing repository facade). Bound against a fixture
/// wired with <see cref="ControllableOutboxExecutor"/> and a
/// <see cref="EdictOutboxOptions.OutboxMaxAttempts"/> of 2.
/// </summary>
public abstract class HandlerFailurePromotesToDeadLetterScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected HandlerFailurePromotesToDeadLetterScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Promotes_ShouldLandRowWithRcaFields()
    {
        var counterId = Guid.NewGuid();
        ControllableOutboxExecutor.Reset();
        ControllableOutboxExecutor.ShouldFail = true;

        await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            await probe.ForceDrainViaReminderAsync();
            return ControllableOutboxExecutor.FailedAttempts >= 2;
        });

        // Heal the controllable so the promoted EdictDeadLetterRaised entry
        // can publish — otherwise it would loop on the same fail/promote
        // cycle and never land the row.
        ControllableOutboxExecutor.ShouldFail = false;

        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await probe.ForceDrainViaReminderAsync();
            return await probe.GetPendingOutboxCountAsync() == 0;
        });

        // The projection writes to its literal "deadletter" table —
        // independent of the fixture's per-collection DeadLetterTableName
        // (which backs the operator-facing repository facade).
        var deadLetterTable = _fixture.GetTableRepository<EdictDeadLetterEntry>(
            EdictDeadLetterTable.Name);

        await WaitUntilAsync(async () =>
        {
            var entries = await deadLetterTable.QueryPartitionAsync(
                EdictDeadLetterTable.Name);
            return entries.Any(e => e.SourceGrainKey.Contains(counterId.ToString()));
        });

        var allEntries = await deadLetterTable.QueryPartitionAsync(
            EdictDeadLetterTable.Name);
        var entry = allEntries.Single(e => e.SourceGrainKey.Contains(counterId.ToString()));

        Assert.Equal("PublishEvent", entry.Kind);
        Assert.Equal(counterId.ToString(), entry.SourceGrainKey);
        Assert.Contains("CounterAggregate", entry.SourceGrainType);
        Assert.Equal("ConformanceCounters/CounterIncrementedEvent", entry.EffectTarget);
        Assert.Equal("System.InvalidOperationException", entry.ExceptionType);
        Assert.Equal("controllable publish failure (outbox conformance test)", entry.Reason);
        Assert.NotNull(entry.PayloadJson);

        await Verify(entry).DontScrubGuids().DontScrubDateTimes()
            .ScrubMember<EdictDeadLetterEntry>(e => e.EntryId)
            .ScrubMember<EdictDeadLetterEntry>(e => e.DeadLetteredAt)
            .ScrubMember<EdictDeadLetterEntry>(e => e.TraceParent)
            .ScrubMember<EdictDeadLetterEntry>(e => e.PayloadJson)
            .ScrubMember<EdictDeadLetterEntry>(e => e.SourceGrainKey)
            .ScrubMember<EdictDeadLetterEntry>(e => e.SourceEventId);
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }
    }
}
