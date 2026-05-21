using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;

using Microsoft.Extensions.Time.Testing;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Outbox;

// ADR 0028: OutboxHost.RegisterDrainReminderAsync reads its period from
// EdictOptions.OutboxDrainReminderPeriod instead of the previous hardcoded
// TimeSpan.FromMinutes(1). The literal was an undocumented floor; the option
// makes the cadence tunable for fleet-wide outage soak tests (PRD user
// story #8). Test through the IReminderRegistrar seam: a failing drain
// schedules the lazy reminder, and the period passed to Orleans must match
// the configured value.
public sealed class OutboxDrainReminderPeriodTests
{
    [Fact]
    public async Task RegisterDrainReminder_ShouldUseConfiguredPeriod()
    {
        var configuredPeriod = TimeSpan.FromMinutes(2);
        var capture = new PeriodCapturingReminderRegistrar();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero));
        var state = new InMemoryPersistentState();
        var options = new EdictOptions
        {
            OutboxDrainReminderPeriod = configuredPeriod,
        };
        var host = new OutboxHost<EdictUnit>(
            state,
            FakeStreamProviderForReminderTest.Instance,
            capture,
            [new AlwaysThrowExecutor()],
            options,
            clock,
            new UnusedPromoterForReminderTest(),
            grainKey: "00000000-0000-0000-0000-000000000000",
            grainTypeName: "FakeHost");

        await host.EnqueueAndDrainAsync([new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.PublishEvent,
            Payload = [1, 2, 3],
        }]);

        Assert.Equal(configuredPeriod, capture.RegisteredPeriod);
        Assert.Equal(configuredPeriod, capture.RegisteredDueTime);
    }

    sealed class PeriodCapturingReminderRegistrar : IReminderRegistrar
    {
        public TimeSpan? RegisteredDueTime { get; private set; }
        public TimeSpan? RegisteredPeriod { get; private set; }

        public Task RegisterOrUpdateReminderAsync(string name, TimeSpan dueTime, TimeSpan period)
        {
            RegisteredDueTime = dueTime;
            RegisteredPeriod = period;
            return Task.CompletedTask;
        }

        public Task UnregisterReminderAsync(string name) => Task.CompletedTask;
    }

    sealed class AlwaysThrowExecutor : IOutboxEffectExecutor
    {
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

        public Task ExecuteAsync(
            OutboxEntry entry,
            IStreamProvider streamProvider,
            Func<EdictEvent, Task>? deferredDispatch,
            Type? consumerType)
            => throw new InvalidOperationException("Forcing reminder registration via drain failure.");
    }

    sealed class InMemoryPersistentState : IPersistentState<GrainEnvelope<EdictUnit>>
    {
        public GrainEnvelope<EdictUnit> State { get; set; } = new();
        public string Etag => string.Empty;
        public bool RecordExists => true;
        public Task WriteStateAsync() => Task.CompletedTask;
        public Task ReadStateAsync() => Task.CompletedTask;
        public Task ClearStateAsync() => Task.CompletedTask;
    }

    sealed class FakeStreamProviderForReminderTest : IStreamProvider
    {
        public static readonly FakeStreamProviderForReminderTest Instance = new();
        public string Name => "edict";
        public bool IsRewindable => false;

        public IAsyncStream<T> GetStream<T>(StreamId streamId) =>
            throw new NotSupportedException();
    }

    sealed class UnusedPromoterForReminderTest : IDeadLetterPromoter
    {
        public OutboxEntry Promote(OutboxEntry failing, Exception exception, string grainKey, string grainTypeName, DateTimeOffset nowUtc) =>
            throw new NotSupportedException("Promotion is not exercised in this test.");
    }
}
