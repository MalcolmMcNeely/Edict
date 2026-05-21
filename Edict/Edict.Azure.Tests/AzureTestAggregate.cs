using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;
using Edict.Core.Idempotency;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using MessagePack;

using Orleans;
using Orleans.Streams;

namespace Edict.Azure.Tests;

// ── Order aggregate (table-projection E2E) ───────────────────────────────────

public sealed partial record AzurePlaceOrderCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [EdictTelemeterized]
    public string Sku { get; init; } = Sku;
}

[EdictStream("AzureOrders")]
public sealed partial record AzureOrderPlacedEvent(Guid OrderId, string Sku) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

public partial class AzureOrderCommandHandler : EdictCommandHandler
{
    public Task<EdictCommandResult> Handle(AzurePlaceOrderCommand command)
    {
        Raise(new AzureOrderPlacedEvent(command.OrderId, command.Sku));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

[GenerateSerializer]
[Alias("Edict.Azure.Tests.AzureOrderTableRow")]
public sealed class AzureOrderTableRow : IEdictPersistedState
{
    [Id(0)]
    public int OrderCount { get; set; }
}

public sealed partial class AzureOrderTableProjectionBuilder : EdictTableProjectionBuilder<AzureOrderTableRow>
{
    public AzureOrderTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
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

[EdictStream("AzureDedupTest")]
public sealed partial record AzureDedupTestEvent(Guid AggregateId, int Sequence) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public int Sequence { get; init; } = Sequence;
}

public interface IAzureDedupTestConsumer : IGrainWithGuidKey
{
    Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync();
    Task ArmThrowOnNextAsync();
    Task DeactivateSelfAsync();
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
public sealed class AzureDedupTestConsumer : EdictIdempotencyBase, IAzureDedupTestConsumer
{
    private readonly List<Guid> _handledEventIds = [];
    private bool _throwOnNext;

    protected override int WindowSize => 3;

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

    public Task DeactivateSelfAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}
