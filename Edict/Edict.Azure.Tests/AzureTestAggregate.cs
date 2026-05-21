using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;
using Edict.Core.Idempotency;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using FluentValidation;
using FluentValidation.Results;

using MessagePack;

using Orleans;
using Orleans.Runtime;
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

public sealed partial record AzureCancelOrderCommand(Guid OrderId, string Reason) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public sealed partial record AzureFailOrderCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public sealed partial record AzureValidateSkuCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

public sealed partial record AzureStateCheckCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public partial class AzureOrderCommandHandler : EdictCommandHandler
{
    public Task<EdictCommandResult> Handle(AzurePlaceOrderCommand command)
    {
        Raise(new AzureOrderPlacedEvent(command.OrderId, command.Sku));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(AzureCancelOrderCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
            [new EdictRejectionReason("already_shipped", "Order has already shipped.")]));

    public Task<EdictCommandResult> Handle(AzureFailOrderCommand command) =>
        throw new InvalidOperationException("simulated failure");

    public Task<EdictCommandResult> Handle(AzureValidateSkuCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

    public Task<EdictCommandResult> Handle(AzureStateCheckCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

    protected override object? GetValidationState() => "grain-active";
}

public sealed class AzureSkuRequiredValidator : AbstractValidator<AzureValidateSkuCommand>
{
    public AzureSkuRequiredValidator()
    {
        RuleFor(x => x.Sku)
            .NotEmpty()
            .WithErrorCode("sku_required")
            .WithMessage("SKU must not be empty.");
    }
}

public sealed class AzureGrainStateRequiredValidator : AbstractValidator<AzureStateCheckCommand>
{
    public AzureGrainStateRequiredValidator()
    {
        RuleFor(x => x).Custom((_, ctx) =>
        {
            if (!ctx.RootContextData.TryGetValue(EdictValidationKeys.GrainState, out var state) || state is null)
            {
                ctx.AddFailure(new ValidationFailure("GrainState", "Grain state was not injected.")
                    { ErrorCode = "missing_state" });
            }
        });
    }
}

[GenerateSerializer]
[Alias("Edict.Azure.Tests.AzureOrderTableRow")]
public sealed class AzureOrderTableRow : IEdictPersistedState
{
    [Id(0)]
    public int OrderCount { get; set; }
}

public interface IAzureOrderTableProjectionProbe : IGrainWithGuidKey
{
    Task<RingStateProbe> GetRingStateAsync();
}

[GenerateSerializer]
[Alias("Edict.Azure.Tests.RingStateProbe")]
public sealed record RingStateProbe(
    [property: Id(0)] int Capacity,
    [property: Id(1)] int Count);

public sealed partial class AzureOrderTableProjectionBuilder
    : EdictTableProjectionBuilder<AzureOrderTableRow>, IAzureOrderTableProjectionProbe
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

    public Task<RingStateProbe> GetRingStateAsync() =>
        Task.FromResult(new RingStateProbe(
            State.Idempotency.HandledEventIds.Length,
            State.Idempotency.Count));
}

/// <summary>
/// Consumer-specified fixed RowKey ("summary"), showing that the RowKey is
/// independent of the PartitionKey.
/// </summary>
public sealed partial class AzureOrderSummaryTableProjectionBuilder : EdictTableProjectionBuilder<AzureOrderTableRow>
{
    public AzureOrderSummaryTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "azureordersummary";

    protected override string GetRowKey(EdictEvent evt) => "summary";

    public Task Handle(AzureOrderPlacedEvent evt)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Global-singleton projection grain. Activated at a fixed Guid key and
/// receives events published directly to its stream key. RowKey = source
/// aggregate ID, so each aggregate's order is a distinct row under the
/// singleton PartitionKey.
/// </summary>
public sealed partial class AzureGlobalOrderTableProjectionBuilder : EdictTableProjectionBuilder<AzureOrderTableRow>
{
    public static readonly Guid SingletonKey = new("00000000-0000-0000-0000-000000000001");

    public AzureGlobalOrderTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "azureglobalorderprojection";

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

// ── In-memory projection (count via grain method) ──────────────────────────

public interface IAzureOrderProjectionAccess : IGrainWithGuidKey
{
    Task<int> GetOrderCountAsync();
}

public sealed partial class AzureOrderProjectionBuilder : EdictProjectionBuilder, IAzureOrderProjectionAccess
{
    int _orderCount;

    public Task<int> GetOrderCountAsync() => Task.FromResult(_orderCount);

    public Task Handle(AzureOrderPlacedEvent evt)
    {
        _orderCount++;
        return Task.CompletedTask;
    }
}

// ── Capture grain: subscribes to "AzureOrders" and buffers events ──────────

public interface IAzureOrderEventCaptureGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync();
}

[ImplicitStreamSubscription("AzureOrders")]
public sealed class AzureOrderEventCaptureGrain : Grain, IAzureOrderEventCaptureGrain
{
    readonly List<EdictEvent> _events = [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("AzureOrders", this.GetPrimaryKey()));
        await stream.SubscribeAsync(
            (item, _) => { _events.Add(item); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync() =>
        Task.FromResult<IReadOnlyList<EdictEvent>>(_events.AsReadOnly());
}

// ── Unhandled event for the "no-op when unhandled" projection test ─────────

[EdictStream("AzureOrders")]
public sealed partial record AzureUnknownOrderEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
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
