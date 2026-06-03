using Edict.Telemetry;

using FluentValidation;
using FluentValidation.Results;

namespace Edict.Core.Tests.Commands;

public sealed class GrainStateRequiredValidator : AbstractValidator<StateCheckCommand>
{
    public GrainStateRequiredValidator()
    {
        RuleFor(x => x).Custom((_, context) =>
        {
            if (!context.RootContextData.TryGetValue(SemanticConventions.Validation.GrainStateKey, out var state) || state is null)
            {
                context.AddFailure(new ValidationFailure("GrainState", "Grain state was not injected.")
                    { ErrorCode = "missing_state" });
            }
        });
    }
}
