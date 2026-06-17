using System.Collections.Concurrent;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Xunit;

namespace Edict.Testing.Tests;

/// <summary>
/// Harness-fidelity guard: the in-process Test Framework must deliver a single
/// aggregate's events to its one projection activation <em>serially</em> — one
/// handler turn completing before the next begins — exactly as a real Orleans
/// pulling agent does (proven against real Azure Queue Streams in
/// <c>Edict.Azure.Streaming.Tests</c>). A high-water mark above one means the
/// harness ran two turns of one activation concurrently, which real Orleans
/// never does and which is what produced the list-projection over-count.
/// </summary>
public sealed class DeliverySerializationTests
{
    [Fact]
    public async Task ManyEventsForOneAggregate_DeliverToProjectionSerially()
    {
        const int eventCount = 5;
        var aggregateId = Guid.NewGuid();
        var tracker = DeliverySerializationProbe.TrackerFor(aggregateId);

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DeliverySerializationTests).Assembly));

        for (var index = 0; index < eventCount; index++)
        {
            await app.SendAsync(new ProbeTickCommand(aggregateId));
        }
        await app.Drain();

        var row = await app.GetProjectionRow<ProbeRow>(
            tableName: "deliveryserializationprobe",
            partitionKey: aggregateId.ToString("N"),
            rowKey: "probe");

        Assert.NotNull(row);
        Assert.Equal(1, tracker.Max);
        Assert.Equal(eventCount, row.TickCount);
    }
}

static class DeliverySerializationProbe
{
    static readonly ConcurrentDictionary<Guid, ConcurrencyTracker> _trackers = new();

    public static ConcurrencyTracker TrackerFor(Guid aggregateId) =>
        _trackers.GetOrAdd(aggregateId, _ => new ConcurrencyTracker());

    // Long enough that a whole burst of events for one aggregate arrives while the
    // first turn is still parked here: if the harness ran a second turn of the
    // activation concurrently, the high-water mark would spike above one.
    public static readonly TimeSpan HandlerHold = TimeSpan.FromMilliseconds(750);

    public sealed class ConcurrencyTracker
    {
        int _active;
        public int Max { get; private set; }

        public void Enter()
        {
            var active = Interlocked.Increment(ref _active);
            // Grain turns are single-threaded, so this read-and-store is safe even
            // when turns interleave: only one turn runs at any instant.
            if (active > Max)
            {
                Max = active;
            }
        }

        public void Exit() => Interlocked.Decrement(ref _active);
    }
}

public sealed partial record ProbeTickCommand(Guid AggregateId) : EdictCommand
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

[EdictStream("DeliverySerializationProbe")]
public sealed partial record ProbeTickEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

public partial class ProbeTickAggregate : EdictCommandHandler
{
    Task<EdictCommandResult> HandleAsync(ProbeTickCommand command)
    {
        Raise(new ProbeTickEvent(command.AggregateId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.ProbeRow")]
public sealed class ProbeRow : IEdictPersistedState
{
    [Id(0)]
    public int TickCount { get; set; }
}

public sealed partial class ProbeProjectionBuilder : EdictListProjectionBuilder<ProbeRow>
{
    public ProbeProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "deliveryserializationprobe";

    protected override string GetRowKey(EdictEvent edictEvent) => "probe";

    async Task HandleAsync(ProbeTickEvent edictEvent)
    {
        var tracker = DeliverySerializationProbe.TrackerFor(edictEvent.AggregateId);
        tracker.Enter();
        try
        {
            await Task.Delay(DeliverySerializationProbe.HandlerHold);
        }
        finally
        {
            tracker.Exit();
        }
        CurrentRow.TickCount++;
    }
}
