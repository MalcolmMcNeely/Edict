namespace Edict.Core.Sagas;

/// <summary>
/// Resolves a saga's absolute lifetime cap into a single <c>DeadlineAt</c>,
/// anchored at the saga's start (its first handled Event) and never reset by
/// later activity. Pure: no Orleans, no clock, no I/O — the start time is an
/// argument so the engine owns the clock seam.
/// <para>
/// Precedence: an explicit <c>[EdictSagaTimeout(Unbounded = true)]</c> opt-out
/// wins outright (the author's escape hatch beats any operator default); a
/// finite <c>[EdictSagaTimeout("T")]</c> applies its own duration; an absent
/// attribute falls back to the silo-wide default, where a null default means
/// unbounded. A null return is "no cap".
/// </para>
/// </summary>
static class SagaDeadlineResolver
{
    public static DateTimeOffset? Resolve(
        SagaTimeoutDeclaration declaration,
        TimeSpan? defaultTimeout,
        DateTimeOffset startedAt)
    {
        if (declaration.IsUnbounded)
        {
            return null;
        }

        if (declaration.Duration is { } explicitDuration)
        {
            return startedAt + explicitDuration;
        }

        return defaultTimeout is { } fallback ? startedAt + fallback : null;
    }
}
