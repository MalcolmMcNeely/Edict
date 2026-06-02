using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Postgres.Tests.Resilience;

// A slow projection over the AQS-streams × Postgres-grain-storage cross. The
// first Handle invocation blocks long enough for the test to KillSiloAsync the
// hosting silo mid-flight; the grain captures the hosting silo's address so the
// test targets the kill precisely. The kill tears the activation down before
// the atomic ring + UpsertRow commit, so the dedup ring never advances and the
// AQS message returns to visible after the fixture's visibility timeout. A
// surviving silo redelivers and the row settles at Count = 1.
[EdictStream("PostgresSiloKillProjection")]
public sealed partial record PostgresSiloKillProjectionEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

[GenerateSerializer]
[Alias("Edict.Postgres.Tests.Resilience.PostgresSiloKillTableRow")]
public sealed class PostgresSiloKillTableRow : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public interface IPostgresSiloKillEventPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent edictEvent);
}

public sealed class PostgresSiloKillEventPublisher : Grain, IPostgresSiloKillEventPublisher
{
    public Task PublishAsync(EdictEvent edictEvent) =>
        this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("PostgresSiloKillProjection", this.GetPrimaryKey()))
            .OnNextAsync(edictEvent);
}

public sealed partial class PostgresSiloKillProjectionBuilder : EdictTableProjectionBuilder<PostgresSiloKillTableRow>
{
    public const string Table = "postgressilokillprojection";

    readonly ILocalSiloDetails _siloDetails;

    public PostgresSiloKillProjectionBuilder(
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
            PostgresSiloKillProjectionEvent e => e.AggregateId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public async Task HandleAsync(PostgresSiloKillProjectionEvent edictEvent)
    {
        var entry = Interlocked.Increment(ref PostgresSiloKillCoordinator.HandlerEntries);
        if (entry == 1)
        {
            PostgresSiloKillCoordinator.HandlerEntered.TrySetResult(_siloDetails.SiloAddress);
            await Task.Delay(TimeSpan.FromSeconds(20));
        }
        CurrentRow.Count++;
    }
}

public static class PostgresSiloKillCoordinator
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
