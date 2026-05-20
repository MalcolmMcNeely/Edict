namespace Edict.Generators;

/// <summary>
/// Single source of truth for fully-qualified type names used by generators and
/// analyzers to match Edict's public surface by FQN (ADR 0005 / ADR 0013).
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

    // ── Edict.Contracts.Telemetry ────────────────────────────────────────────
    public const string EdictTelemeterizedAttributeFqn =
        "global::Edict.Contracts.Telemetry.EdictTelemeterizedAttribute";

    // ── Edict.Telemetry ──────────────────────────────────────────────────────
    public const string EdictDiagnosticsActivitySourceFqn =
        "global::Edict.Telemetry.EdictDiagnostics.ActivitySource";

    public const string ActivitySourceExtensionsFqn =
        "global::Edict.Telemetry.ActivitySourceExtensions";

    public const string ActivityExtensionsFqn =
        "global::Edict.Telemetry.ActivityExtensions";

    // ── Edict.Core.Commands ──────────────────────────────────────────────────
    public const string EdictCommandHandlerFqn =
        "global::Edict.Core.Commands.EdictCommandHandler";

    // ── Edict.Core.Projections ───────────────────────────────────────────────
    public const string EdictProjectionBuilderFqn =
        "global::Edict.Core.Projections.EdictProjectionBuilder";

    // ── Edict.Core.EventHandler ──────────────────────────────────────────────
    // EdictEventHandler closes EdictIdempotencyBase<EdictUnit> via the
    // payload-free shim, so the consumer's `partial class : EdictEventHandler`
    // shape is matched by FQN with no generic-arity considerations (mirrors
    // EdictProjectionBuilder).
    public const string EdictEventHandlerFqn =
        "global::Edict.Core.EventHandler.EdictEventHandler";

    // ── Edict.Core.Saga ──────────────────────────────────────────────────────
    // EdictSaga is generic; matched via a generics-stripped FQN base-chain walk
    // (mirrors EdictCommandHandler), so this name carries no `1 arity suffix.
    public const string EdictSagaFqn =
        "global::Edict.Core.Saga.EdictSaga";

    public const string IEdictSagaFqn =
        "global::Edict.Core.Saga.IEdictSaga";

    // ── System ───────────────────────────────────────────────────────────────
    public const string TaskFqn =
        "global::System.Threading.Tasks.Task";

    public const string TaskOfEdictCommandResultFqn =
        "global::System.Threading.Tasks.Task<global::Edict.Contracts.Commands.EdictCommandResult>";
}
