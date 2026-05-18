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

    // ── Edict.Contracts.Events ───────────────────────────────────────────────
    public const string EdictEventFqn =
        "global::Edict.Contracts.Events.EdictEvent";

    public const string EdictStreamAttributeFqn =
        "global::Edict.Contracts.Events.EdictStreamAttribute";

    // ── Edict.Contracts.Results ──────────────────────────────────────────────
    public const string EdictCommandResultFqn =
        "global::Edict.Contracts.Results.EdictCommandResult";

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

    // ── Edict.Core.Grains ────────────────────────────────────────────────────
    public const string EdictCommandHandlerGrainFqn =
        "global::Edict.Core.Grains.EdictCommandHandlerGrain";

    public const string EdictProjectionBuilderGrainFqn =
        "global::Edict.Core.Grains.EdictProjectionBuilderGrain";

    // ── System ───────────────────────────────────────────────────────────────
    public const string TaskFqn =
        "global::System.Threading.Tasks.Task";

    public const string TaskOfEdictCommandResultFqn =
        "global::System.Threading.Tasks.Task<global::Edict.Contracts.Results.EdictCommandResult>";
}
