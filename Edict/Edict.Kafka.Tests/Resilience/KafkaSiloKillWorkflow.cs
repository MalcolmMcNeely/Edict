using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Orleans;
using Orleans.Runtime;

namespace Edict.Kafka.Tests.Resilience;

// Dedicated event types for the Kafka silo-kill suite. The dead-letter and
// projection rows live in Postgres (via AddEdictPostgresPersistence) so a
// silo restart can prove redelivery + the EdictTableProjectionBuilder atomic
// ring-equals-row commit holds the projection row at the same count even
// when the handler is invoked twice end-to-end.
//
// Single-event stream for mid-handler crash; multi-event stream for
// mid-batch crash. They share the projection state shape but ride different
// streams so test ordering / interleaving does not cross-contaminate.

[EdictStream(StreamName)]
public sealed partial record KafkaSiloKillEvent(Guid AggregateId) : EdictEvent
{
    public const string StreamName = "KafkaSiloKill";

    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

[EdictStream(StreamName)]
public sealed partial record KafkaSiloKillBatchEvent(Guid AggregateId, int Sequence) : EdictEvent
{
    public const string StreamName = "KafkaSiloKillBatch";

    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public int Sequence { get; init; } = Sequence;
}

[GenerateSerializer]
[Alias("Edict.Kafka.Tests.Resilience.KafkaSiloKillTableRow")]
public sealed class KafkaSiloKillTableRow : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public interface IKafkaSiloKillEventPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent edictEvent);
}

public sealed class KafkaSiloKillEventPublisher : Grain, IKafkaSiloKillEventPublisher
{
    public Task PublishAsync(EdictEvent edictEvent) =>
        this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create(KafkaSiloKillEvent.StreamName, this.GetPrimaryKey()))
            .OnNextAsync(edictEvent);
}

public interface IKafkaSiloKillBatchEventPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent edictEvent);
}

public sealed class KafkaSiloKillBatchEventPublisher : Grain, IKafkaSiloKillBatchEventPublisher
{
    public Task PublishAsync(EdictEvent edictEvent) =>
        this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create(KafkaSiloKillBatchEvent.StreamName, this.GetPrimaryKey()))
            .OnNextAsync(edictEvent);
}

// Slow projection — the first delivery of a KafkaSiloKillEvent blocks long
// enough for the test to KillSiloAsync the hosting silo, dropping the
// uncommitted Kafka offset (enable.auto.commit=false floor). On redelivery
// the projection commits the row exactly once because the EdictIdempotencyBase
// ring slot and the UpsertRow effect commit atomically in one grain-state
// write — only present if the first delivery had returned, which it didn't.
public sealed partial class KafkaSiloKillProjectionBuilder : EdictTableProjectionBuilder<KafkaSiloKillTableRow>
{
    public const string Table = "kafkasilokillprojection";

    readonly ILocalSiloDetails _siloDetails;

    public KafkaSiloKillProjectionBuilder(
        IEdictTableStoreFactory storeFactory,
        ILocalSiloDetails siloDetails)
        : base(storeFactory)
    {
        _siloDetails = siloDetails;
    }

    protected override string TableName => Table;

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            KafkaSiloKillEvent e => e.AggregateId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public async Task Handle(KafkaSiloKillEvent edictEvent)
    {
        var entry = Interlocked.Increment(ref KafkaSiloKillCoordinator.HandlerEntries);
        if (entry == 1)
        {
            // First delivery: announce hosting silo and block long enough for
            // the test to KillSiloAsync before Handle returns. The kill tears
            // down the activation so the UpsertRow effect is never staged and
            // the ring never commits — Kafka's last-committed offset is
            // still before this event, so the next consumer redelivers it.
            KafkaSiloKillCoordinator.HandlerEntered.TrySetResult(_siloDetails.SiloAddress);
            await Task.Delay(TimeSpan.FromSeconds(20));
        }
        CurrentRow.Count++;
    }
}

// Same shape as KafkaSiloKillProjectionBuilder but bound to the batch stream.
// The first delivery of sequence=1 blocks; the test publishes sequence=2 a
// moment later so both events land in one Kafka poll. Kill while Handle on
// sequence=1 is blocked → MessagesDeliveredAsync never runs → both events'
// offsets stay uncommitted → both redelivered. After restart, both Handle
// fresh and the row settles at Count=2 (one increment per event).
public sealed partial class KafkaSiloKillBatchProjectionBuilder : EdictTableProjectionBuilder<KafkaSiloKillTableRow>
{
    public const string Table = "kafkasilokillbatchprojection";

    readonly ILocalSiloDetails _siloDetails;

    public KafkaSiloKillBatchProjectionBuilder(
        IEdictTableStoreFactory storeFactory,
        ILocalSiloDetails siloDetails)
        : base(storeFactory)
    {
        _siloDetails = siloDetails;
    }

    protected override string TableName => Table;

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            KafkaSiloKillBatchEvent e => e.AggregateId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public async Task Handle(KafkaSiloKillBatchEvent edictEvent)
    {
        var entry = Interlocked.Increment(ref KafkaSiloKillBatchCoordinator.HandlerEntries);
        if (edictEvent.Sequence == 1 && entry == 1)
        {
            KafkaSiloKillBatchCoordinator.HandlerEntered.TrySetResult(_siloDetails.SiloAddress);
            await Task.Delay(TimeSpan.FromSeconds(20));
        }
        CurrentRow.Count++;
    }
}

public static class KafkaSiloKillCoordinator
{
    public static TaskCompletionSource<SiloAddress> HandlerEntered { get; private set; }
        = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static int HandlerEntries;

    public static void Reset()
    {
        HandlerEntries = 0;
        HandlerEntered = new TaskCompletionSource<SiloAddress>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static Task<SiloAddress> WaitForHandlerEnteredAsync(TimeSpan timeout) =>
        HandlerEntered.Task.WaitAsync(timeout);
}

public static class KafkaSiloKillBatchCoordinator
{
    public static TaskCompletionSource<SiloAddress> HandlerEntered { get; private set; }
        = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static int HandlerEntries;

    public static void Reset()
    {
        HandlerEntries = 0;
        HandlerEntered = new TaskCompletionSource<SiloAddress>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static Task<SiloAddress> WaitForHandlerEnteredAsync(TimeSpan timeout) =>
        HandlerEntered.Task.WaitAsync(timeout);
}
