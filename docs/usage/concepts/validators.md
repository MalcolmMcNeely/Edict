# Command validators

An `EdictCommandValidator<TCommand>` is a server-side, no-mutation precondition gate for one command — authored with FluentValidation's `RuleFor` / `When` / `WithErrorCode` DSL and run inside the same Orleans activation turn as the command handler, before `HandleAsync`.

```csharp
using Edict.Core.Commands;

using FluentValidation;

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
```

A failing rule short-circuits the dispatch and returns `EdictCommandResult.Rejected`, one `EdictRejectionReason` per `ValidationFailure`. The handler never runs and no state mutation occurs. Validators never throw for business rejection.

## Surface

- **`EdictCommandValidator<TCommand>`** (`Edict.Core.Commands`) — abstract base where `TCommand : EdictCommand`. Derives from `FluentValidation.AbstractValidator<TCommand>` and adds no members; the DSL surface is FluentValidation's. A consumer authors a `sealed class {Name}CommandValidator : EdictCommandValidator<TCommand>` and adds `RuleFor(...)` calls in the constructor.
- **`RuleFor(expression)`** — FluentValidation's rule-builder entry point. Pair each rule with `WithErrorCode("snake_case_code")` so the surfaced `EdictRejectionReason.Code` is stable and machine-branchable.
- **`WithErrorCode(string)`** — sets the `ValidationFailure.ErrorCode` the framework maps onto `EdictRejectionReason.Code`. Without it, the code is `"validation_error"` — strongly preferred to supply a meaningful code per rule.
- **`ValidationContext.RootContextData[SemanticConventions.Validation.GrainStateKey]`** — the key under which the framework stamps the current aggregate state for the validator to inspect. A command handler exposes state by overriding `protected virtual object? GetValidationState()`; a validator reads via `RuleFor(x => x).Custom((command, context) => { var state = ((TState?)context.RootContextData[SemanticConventions.Validation.GrainStateKey]); ... })`. The default `GetValidationState()` returns `null` — no state injected — so rules that only inspect the command payload need no extra wiring on either side.
- **Auto-discovery** — `AddEdict(assembly)` registers every `EdictCommandValidator<TCommand>` derivative in the scanned assemblies through FluentValidation's `AddValidatorsFromAssemblies`. No manual DI registration is required.

A validator is a stateless DI service, not a grain. It may read state via `RootContextData`; it must never mutate. The single-activation guarantee means the state the validator inspects cannot be raced before `HandleAsync` acts — a correctness guarantee client-side validation cannot offer.

## Analyzer rules

- **EDICT019** — at most one `EdictCommandValidator<TCommand>` derivative per command type (compilation-end check). Validators are auto-discovered, so a duplicate registration is last-wins at DI build time and the silent loser ships dead code. The rule turns that into a build error.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Command Validator`, `EdictCommand`, `Command Result`.
- Concepts — [commands.md](commands.md), [dead-letter.md](dead-letter.md), [telemetry.md](telemetry.md).
- Decision trail — [ADR-0008](../../adr/0008-command-validator-precondition-gate.md), [ADR-0048](../../adr/0048-edict-owned-base-for-command-validator.md).
