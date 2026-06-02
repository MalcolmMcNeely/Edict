namespace Edict.Core.Sagas;

/// <summary>
/// The per-saga timeout cap as declared on the saga class, read from the
/// <c>[EdictSagaTimeout]</c> attribute (or its absence) and handed to
/// <see cref="SagaDeadlineResolver"/>. A finite <see cref="Duration"/>, an
/// explicit opt-out (<see cref="IsUnbounded"/>), or neither (the attribute is
/// absent, so the silo-wide default applies).
/// </summary>
readonly record struct SagaTimeoutDeclaration
{
    SagaTimeoutDeclaration(TimeSpan? duration, bool isUnbounded)
    {
        Duration = duration;
        IsUnbounded = isUnbounded;
    }

    public TimeSpan? Duration { get; }

    public bool IsUnbounded { get; }

    /// <summary>No <c>[EdictSagaTimeout]</c> on the saga — the silo-wide default decides.</summary>
    public static SagaTimeoutDeclaration None => new(null, false);

    /// <summary><c>[EdictSagaTimeout("T")]</c> — a finite cap that wins over the default.</summary>
    public static SagaTimeoutDeclaration ForDuration(TimeSpan duration) => new(duration, false);

    /// <summary><c>[EdictSagaTimeout(Unbounded = true)]</c> — opts out, beating even a finite default.</summary>
    public static SagaTimeoutDeclaration AsUnbounded => new(null, true);
}
