using Edict.Core.Commands;

using FluentValidation;
using FluentValidation.Results;

namespace Edict.Core.Tests.Grains;

// Validators registered in EdictClusterFixture.SiloConfigurator for the
// Command Validator integration tests (issue #12). Kept stateless per ADR 0009.

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
                ctx.AddFailure(new ValidationFailure("GrainState", "Grain state was not injected.")
                    { ErrorCode = "missing_state" });
        });
    }
}
