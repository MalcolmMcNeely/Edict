using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Tests.TestSupport;

using Microsoft.Extensions.Time.Testing;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Outbox;

// A grain-state write fault leaves the activation's in-memory Payload / dedup
// ring / outbox diverged from the last durable snapshot. The host requests
// deactivation at the single write seam so the dirty activation is dropped and a
// redelivery reloads clean durable state — the only thing that can roll back an
// arbitrary consumer mutation. Drives the host directly, the way the grain roots do.
public sealed class DeactivateOnWriteFaultTests
{
    static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    static readonly EdictOptions Options = new();

    [Fact]
    public async Task WriteFaultDuringCommit_RequestsDeactivation()
    {
        // Arrange
        var state = new FaultInjectingPersistentState { FailOnWrite = 1 };
        var deactivationRequests = 0;
        var host = BuildHost(state, () => deactivationRequests++);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.EnqueueAndDrainAsync([SendCommandEntry()]));

        // Assert
        Assert.Equal(1, deactivationRequests);
    }

    [Fact]
    public async Task WriteSucceeds_DoesNotRequestDeactivation()
    {
        // Arrange
        var state = new FaultInjectingPersistentState();
        var deactivationRequests = 0;
        var host = BuildHost(state, () => deactivationRequests++);

        // Act
        await host.EnqueueAndDrainAsync([SendCommandEntry()]);

        // Assert
        Assert.Equal(0, deactivationRequests);
    }

    static OutboxHost<EdictUnit> BuildHost(
        IPersistentState<GrainEnvelope<EdictUnit>> state,
        Action requestDeactivation) =>
        new(
            state,
            NullStreamProvider.Instance,
            new RecordingReminderRegistrar(new CallLog()),
            [new RecordingExecutor(OutboxEffectKind.SendCommand)],
            Options,
            new FakeTimeProvider(Now),
            new NoopPromoter(),
            grainKey: "test-grain",
            grainTypeName: "TestGrain",
            requestDeactivation: requestDeactivation);

    static OutboxEntry SendCommandEntry() => new()
    {
        EntryId = Guid.NewGuid(),
        Kind = OutboxEffectKind.SendCommand,
        Payload = [],
    };

    sealed class RecordingExecutor(OutboxEffectKind kind) : IOutboxEffectExecutor
    {
        public OutboxEffectKind Kind => kind;

        public Task<OutboxEntry?> ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent) =>
            Task.FromResult<OutboxEntry?>(null);
    }

    sealed class NoopPromoter : IDeadLetterPromoter
    {
        public OutboxEntry Promote(OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now) =>
            failed with { Kind = OutboxEffectKind.PublishEvent };
    }

    sealed class NullStreamProvider : IStreamProvider
    {
        public static readonly NullStreamProvider Instance = new();
        public string Name => "edict";
        public bool IsRewindable => false;
        public IAsyncStream<T> GetStream<T>(StreamId streamId) =>
            throw new NotSupportedException("NullStreamProvider has no streams.");
    }
}
