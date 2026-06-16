namespace Edict.Generators;

/// <summary>
/// Single source of truth for fully-qualified type names used by generators and
/// analyzers to match Edict's public surface by FQN.
/// Referenced by both generator assemblies when they split; do not inline these
/// strings into individual files.
/// </summary>
internal static class EdictWellKnownNames
{
    // ── Edict.Contracts.Commands ─────────────────────────────────────────────
    public const string EdictCommandFqn =
        "global::Edict.Contracts.Commands.EdictCommand";

    public const string EdictRouteKeyAttributeFqn =
        "global::Edict.Contracts.Commands.EdictRouteKeyAttribute";

    public const string EdictCommandResultFqn =
        "global::Edict.Contracts.Commands.EdictCommandResult";

    // ── Edict.Contracts.Events ───────────────────────────────────────────────
    public const string EdictEventFqn =
        "global::Edict.Contracts.Events.EdictEvent";

    public const string EdictStreamAttributeFqn =
        "global::Edict.Contracts.Events.EdictStreamAttribute";

    // ── Edict.Contracts.Schedules ────────────────────────────────────────────
    public const string EdictScheduleMessageFqn =
        "global::Edict.Contracts.Schedules.EdictScheduleMessage";

    // ── Edict.Contracts.Telemetry ────────────────────────────────────────────
    public const string EdictTelemeterizedAttributeFqn =
        "global::Edict.Contracts.Telemetry.EdictTelemeterizedAttribute";

    // Generators can't reference the runtime Edict.Telemetry assembly, so the
    // Telemeterized tag prefix lives here as a literal. An
    // Edict.Architecture.Tests fact asserts equality with
    // SemanticConventions.Telemeterized.Prefix so the two can't drift silently.
    public const string TelemeterizedTagPrefix = "edict.";

    // ── Edict.Telemetry ──────────────────────────────────────────────────────
    public const string EdictDiagnosticsActivitySourceFqn =
        "global::Edict.Telemetry.EdictDiagnostics.ActivitySource";

    public const string ActivitySourceExtensionsFqn =
        "global::Edict.Telemetry.ActivitySourceExtensions";

    public const string ActivityExtensionsFqn =
        "global::Edict.Telemetry.ActivityExtensions";

    public const string IEventTagWritersFqn =
        "global::Edict.Telemetry.IEventTagWriters";

    // ── Handler discovery ────────────────────────────────────────────────────
    // Single source of truth for the method name the source generators and
    // analyzers look up on consumer handler bases. Compile-linked into
    // Edict.Analyzers and Edict.Mcp; the next convention change is a one-line
    // edit, and the parity test in Edict.Analyzers.Tests fails if any consumer
    // assembly drifts off this value.
    public const string HandleMethodName = "HandleAsync";

    // The schedule-timeout compensation hook the consumer optionally writes per
    // schedule message; the spine emitter type-switches over its overloads.
    public const string OnScheduleTimeoutMethodName = "OnScheduleTimeoutAsync";

    // ── Edict.Core.Commands ──────────────────────────────────────────────────
    public const string EdictCommandHandlerFqn =
        "global::Edict.Core.Commands.EdictCommandHandler";

    // EdictCommandValidator is open-generic (`1); matched via a generics-stripped
    // FQN base-chain walk (mirrors EdictSaga), so this name carries no `1 arity
    // suffix.
    public const string EdictCommandValidatorFqn =
        "global::Edict.Core.Commands.EdictCommandValidator";

    // ── Edict.Core.Projections ───────────────────────────────────────────────
    // The renamed abstract root both projection species derive from. Generics
    // stripped, so both EdictListProjectionBuilder<TListProjection> (closing the
    // root on EdictUnit) and the in-grain EdictProjectionBuilder<TProjection>
    // (closing it on the projection type) classify. Leaving the classifier
    // pointed at the bare EdictProjectionBuilder name would silently stop the
    // List species from being generated — it compiles, emits nothing, no
    // diagnostic.
    public const string EdictProjectionBuilderBaseFqn =
        "global::Edict.Core.Projections.EdictProjectionBuilderBase";

    // The concrete in-grain (State) species. Generics stripped, so the open
    // generic EdictProjectionBuilder<TProjection> matches; the projection type
    // (its sole type arg) keys the read facade's route.
    public const string EdictStateProjectionBuilderFqn =
        "global::Edict.Core.Projections.EdictProjectionBuilder";

    // The concrete List species. Generics stripped, so the open generic
    // EdictListProjectionBuilder<TListProjection> matches; the row type is its
    // sole type arg.
    public const string EdictListProjectionBuilderFqn =
        "global::Edict.Core.Projections.EdictListProjectionBuilder";

    // ── Edict.Core.EventHandler ──────────────────────────────────────────────
    // EdictEventHandler closes EdictIdempotencyBase<EdictUnit> via the
    // payload-free shim, so the consumer's `partial class : EdictEventHandler`
    // shape is matched by FQN with no generic-arity considerations (mirrors
    // EdictProjectionBuilder).
    public const string EdictEventHandlerFqn =
        "global::Edict.Core.EventHandler.EdictEventHandler";

    // ── Edict.Core.Sagas ─────────────────────────────────────────────────────
    // EdictSaga is generic; matched via a generics-stripped FQN base-chain walk
    // (mirrors EdictCommandHandler), so this name carries no `1 arity suffix.
    public const string EdictSagaFqn =
        "global::Edict.Core.Sagas.EdictSaga";

    public const string IEdictSagaFqn =
        "global::Edict.Core.Sagas.IEdictSaga";

    // ── Edict.Contracts.Sagas ────────────────────────────────────────────────
    public const string EdictSagaTimeoutAttributeFqn =
        "global::Edict.Contracts.Sagas.EdictSagaTimeoutAttribute";

    // ── Edict.Contracts.Persistence ──────────────────────────────────────────
    public const string IEdictPersistedStateFqn =
        "global::Edict.Contracts.Persistence.IEdictPersistedState";

    // ── Edict.Contracts.Tenancy ──────────────────────────────────────────────
    public const string EdictTenantScopedAttributeFqn =
        "global::Edict.Contracts.Tenancy.EdictTenantScopedAttribute";

    // ── Edict.Contracts.Sending ──────────────────────────────────────────────
    public const string IEdictSenderFqn =
        "global::Edict.Contracts.Sending.IEdictSender";

    // ── Edict.Core.Commands.EdictSender ──────────────────────────────────────
    public const string EdictSenderFqn =
        "global::Edict.Core.Commands.EdictSender";

    // ── Orleans serialization attributes ─────────────────────────────────────
    public const string OrleansGenerateSerializerAttributeFqn =
        "global::Orleans.GenerateSerializerAttribute";

    public const string OrleansAliasAttributeFqn =
        "global::Orleans.AliasAttribute";

    public const string OrleansIdAttributeFqn =
        "global::Orleans.IdAttribute";

    // ── System ───────────────────────────────────────────────────────────────
    public const string TaskFqn =
        "global::System.Threading.Tasks.Task";

    public const string TaskOfEdictCommandResultFqn =
        "global::System.Threading.Tasks.Task<global::Edict.Contracts.Commands.EdictCommandResult>";

    public const string TaskOfEdictDispatchOutcomeFqn =
        "global::System.Threading.Tasks.Task<global::Edict.Core.Idempotency.EdictDispatchOutcome>";

    public const string TaskOfEdictScheduleResultFqn =
        "global::System.Threading.Tasks.Task<global::Edict.Contracts.Schedules.EdictScheduleResult>";

    public const string TaskOfBoolFqn =
        "global::System.Threading.Tasks.Task<bool>";

    public const string EdictDispatchOutcomeNotHandledFqn =
        "global::Edict.Core.Idempotency.EdictDispatchOutcome.NotHandled";
}
