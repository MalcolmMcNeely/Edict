using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;
using Edict.Core.Projections;
using Edict.Core.TableStorage;
using Edict.Telemetry;

using FluentValidation;
using FluentValidation.Results;

using Orleans;

namespace Edict.Azure.Tests;

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
    public Task<EdictCommandResult> HandleAsync(AzurePlaceOrderCommand command)
    {
        Raise(new AzureOrderPlacedEvent(command.OrderId, command.Sku));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> HandleAsync(AzureCancelOrderCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
            [new EdictRejectionReason("already_shipped", "Order has already shipped.")]));

    public Task<EdictCommandResult> HandleAsync(AzureFailOrderCommand command) =>
        throw new InvalidOperationException("simulated failure");

    public Task<EdictCommandResult> HandleAsync(AzureValidateSkuCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

    public Task<EdictCommandResult> HandleAsync(AzureStateCheckCommand command) =>
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
            if (!ctx.RootContextData.TryGetValue(SemanticConventions.Validation.GrainStateKey, out var state) || state is null)
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

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            AzureOrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task HandleAsync(AzureOrderPlacedEvent edictEvent)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }

    public Task<RingStateProbe> GetRingStateAsync() =>
        Task.FromResult(new RingStateProbe(
            State.Idempotency.HandledEventIds.Length,
            State.Idempotency.Count));
}

// Consumer-specified fixed RowKey ("summary") — proves RowKey is independent
// of PartitionKey.
public sealed partial class AzureOrderSummaryTableProjectionBuilder : EdictTableProjectionBuilder<AzureOrderTableRow>
{
    public AzureOrderSummaryTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "azureordersummary";

    protected override string GetRowKey(EdictEvent edictEvent) => "summary";

    public Task HandleAsync(AzureOrderPlacedEvent edictEvent)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }
}

// Global-singleton projection grain at a fixed Guid key. RowKey is the
// source aggregate ID, so each aggregate's order is a distinct row under
// the singleton PartitionKey.
public sealed partial class AzureGlobalOrderTableProjectionBuilder : EdictTableProjectionBuilder<AzureOrderTableRow>
{
    public static readonly Guid SingletonKey = new("00000000-0000-0000-0000-000000000001");

    public AzureGlobalOrderTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "azureglobalorderprojection";

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            AzureOrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task HandleAsync(AzureOrderPlacedEvent edictEvent)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }
}

public interface IAzureOrderProjectionAccess : IGrainWithGuidKey
{
    Task<int> GetOrderCountAsync();
}

public sealed partial class AzureOrderProjectionBuilder : EdictProjectionBuilder, IAzureOrderProjectionAccess
{
    int _orderCount;

    public Task<int> GetOrderCountAsync() => Task.FromResult(_orderCount);

    public Task HandleAsync(AzureOrderPlacedEvent edictEvent)
    {
        _orderCount++;
        return Task.CompletedTask;
    }
}

[EdictStream("AzureOrders")]
public sealed partial record AzureUnknownOrderEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

