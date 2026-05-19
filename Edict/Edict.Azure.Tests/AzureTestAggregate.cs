using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Results;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;
using Edict.Core.Dedup;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using MessagePack;

using Orleans;
using Orleans.Streams;

namespace Edict.Azure.Tests;

// ── Order aggregate (table-projection E2E) ───────────────────────────────────

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record AzurePlaceOrderCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [EdictTelemeterized]
    public string Sku { get; init; } = Sku;
}

[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("AzureOrders")]
public sealed partial record AzureOrderPlacedEvent(Guid OrderId, string Sku) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

public partial class AzureOrderGrain : EdictCommandHandlerGrain
{
    public Task<EdictCommandResult> Handle(AzurePlaceOrderCommand command)
    {
        Raise(new AzureOrderPlacedEvent(command.OrderId, command.Sku));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

public sealed class AzureOrderTableRow
{
    public int OrderCount { get; set; }
}

public sealed partial class AzureOrderTableProjectionGrain : EdictTableProjectionBuilderGrain<AzureOrderTableRow>
{
    public AzureOrderTableProjectionGrain(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "azureorderprojection";

    protected override string GetRowKey(EdictEvent evt) =>
        evt switch
        {
            AzureOrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task Handle(AzureOrderPlacedEvent evt)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }
}

// ── Dedup aggregate (at-least-once + dedup realism proof) ───────────────────

[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("AzureDedupTest")]
public sealed partial record AzureDedupTestEvent(Guid AggregateId, int Sequence) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public int Sequence { get; init; } = Sequence;
}

public interface IAzureDedupTestGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync();
    Task ArmThrowOnNextAsync();
}

public interface IAzureDedupPublisherGrain : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent evt);
}

public sealed class AzureDedupPublisherGrain : Grain, IAzureDedupPublisherGrain
{
    public Task PublishAsync(EdictEvent evt)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("AzureDedupTest", this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }
}

[ImplicitStreamSubscription("AzureDedupTest")]
public sealed class AzureDedupTestGrain : EdictEventDeduplicationGrain, IAzureDedupTestGrain
{
    private readonly List<Guid> _handledEventIds = [];
    private bool _throwOnNext;

    protected override int RingSize => 3;

    protected override async Task SubscribeToStreamAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("AzureDedupTest", this.GetPrimaryKey()));
        await stream.SubscribeAsync(OnStreamEventAsync, static _ => Task.CompletedTask);
    }

    protected override Task<bool> DispatchAsync(EdictEvent evt)
    {
        if (evt is not AzureDedupTestEvent dedupEvt)
            return Task.FromResult(false);

        if (_throwOnNext)
        {
            _throwOnNext = false;
            throw new InvalidOperationException("simulated dispatch failure");
        }

        _handledEventIds.Add(dedupEvt.EventId);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync() =>
        Task.FromResult<IReadOnlyList<Guid>>(_handledEventIds.AsReadOnly());

    public Task ArmThrowOnNextAsync()
    {
        _throwOnNext = true;
        return Task.CompletedTask;
    }
}
