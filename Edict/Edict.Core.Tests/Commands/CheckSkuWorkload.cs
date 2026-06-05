using Edict.Contracts.Commands;
using Edict.Core.Commands;

using FluentValidation;

namespace Edict.Core.Tests.Commands;

// A command with its own validator carrying a custom error code, so the
// validation-rejection facts can prove the FluentValidation error code maps
// onto EdictRejectionReason.Code. ValidateSkuCommand already has an
// auto-discovered consumer validator in this assembly (ValidatorAutoDiscoveryTests),
// so the rejection facts use a distinct command to avoid two validators racing
// for the same type.
public sealed partial record CheckSkuCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

public partial class CheckSkuCommandHandler : EdictCommandHandler
{
    Task<EdictCommandResult> HandleAsync(CheckSkuCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
}

public sealed class CheckSkuRequiredValidator : AbstractValidator<CheckSkuCommand>
{
    public CheckSkuRequiredValidator()
    {
        RuleFor(x => x.Sku)
            .NotEmpty()
            .WithErrorCode("sku_required")
            .WithMessage("SKU must not be empty.");
    }
}
