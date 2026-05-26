using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;

using FluentValidation;
using FluentValidation.Results;

namespace Edict.Tests.Conformance;

/// <summary>
/// Substrate-neutral commands, events, handler and validators exercised by
/// the abstract conformance scenarios. Each provider's fixture registers the
/// validators against its silo so <see cref="OrderCommandHandler"/> sees the
/// expected validation surface.
/// </summary>
public sealed partial record PlaceOrderCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [EdictTelemeterized]
    public string Sku { get; init; } = Sku;
}

[EdictStream("ConformanceOrders")]
public sealed partial record OrderPlacedEvent(Guid OrderId, string Sku) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

public sealed partial record CancelOrderCommand(Guid OrderId, string Reason) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public sealed partial record ValidateSkuCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

public sealed partial record StateCheckCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public partial class OrderCommandHandler : EdictCommandHandler
{
    public Task<EdictCommandResult> Handle(PlaceOrderCommand command)
    {
        Raise(new OrderPlacedEvent(command.OrderId, command.Sku));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(CancelOrderCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
            [new EdictRejectionReason("already_shipped", "Order has already shipped.")]));

    public Task<EdictCommandResult> Handle(ValidateSkuCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

    public Task<EdictCommandResult> Handle(StateCheckCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

    protected override object? GetValidationState() => "grain-active";
}

public sealed class SkuRequiredValidator : AbstractValidator<ValidateSkuCommand>
{
    public SkuRequiredValidator()
    {
        RuleFor(x => x.Sku)
            .NotEmpty()
            .WithErrorCode("sku_required")
            .WithMessage("SKU must not be empty.");
    }
}

public sealed class GrainStateRequiredValidator : AbstractValidator<StateCheckCommand>
{
    public GrainStateRequiredValidator()
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
