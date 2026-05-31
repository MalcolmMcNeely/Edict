using Edict.Core.Commands;

using FluentValidation;

using Sample.Contracts.Orders.Commands;

namespace Sample.Domain.Orders.Validators;

public sealed class OrderPlaceCommandValidator : EdictCommandValidator<PlaceOrderCommand>
{
    public OrderPlaceCommandValidator()
    {
        RuleFor(x => x.CustomerReference)
            .NotEmpty()
            .WithErrorCode("customer_reference_required")
            .WithMessage("CustomerReference must not be empty.");
    }
}
