using Edict.Core.Commands;
using Edict.Telemetry;

using FluentValidation;
using FluentValidation.Results;

using Sample.Contracts.Cart.Commands;
using Sample.Domain.Cart.State;

namespace Sample.Domain.Cart.Validators;

// Gates CheckoutCart before HandleAsync runs. An empty basket is a state
// pre-condition, so the rule reads the CartState the framework injects into the
// validation context rather than any field on the Command itself.
public sealed class CartCheckoutCommandValidator : EdictCommandValidator<CheckoutCartCommand>
{
    public CartCheckoutCommandValidator()
    {
        RuleFor(command => command).Custom((_, context) =>
        {
            if (context.RootContextData.TryGetValue(SemanticConventions.Validation.GrainStateKey, out var state)
                && state is CartState cartState
                && cartState.Skus.Count == 0)
            {
                context.AddFailure(new ValidationFailure("Skus", "Cart has no items to check out.")
                    { ErrorCode = "empty_cart" });
            }
        });
    }
}
