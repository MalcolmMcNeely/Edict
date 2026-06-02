namespace Edict.Contracts.Sagas;

/// <summary>
/// Declares a saga's absolute lifetime cap: a fixed deadline armed once when
/// the saga starts (its first handled Event) and never reset by later activity.
/// Either a <see cref="Duration"/> (a <c>TimeSpan</c>-parseable literal such as
/// <c>"24:00:00"</c>) or <see cref="Unbounded"/> — never both. Absent, the saga
/// inherits the silo-wide default (ships finite at 7 days). When the cap fires,
/// the framework gives the saga one chance to compensate; an un-overridden
/// timeout dead-letters so a stuck saga surfaces loudly. Consumer-facing, so
/// brand-prefixed.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class EdictSagaTimeoutAttribute : Attribute
{
    /// <summary>Opts the saga out of any cap — beats even a finite silo-wide default.</summary>
    public EdictSagaTimeoutAttribute()
    {
    }

    /// <summary>Declares a finite cap from a <c>TimeSpan</c>-parseable literal (e.g. <c>"24:00:00"</c>).</summary>
    public EdictSagaTimeoutAttribute(string duration)
    {
        Duration = duration;
    }

    /// <summary>The cap as a <c>TimeSpan</c>-parseable literal, or null when only <see cref="Unbounded"/> is set.</summary>
    public string? Duration { get; }

    /// <summary>When true, the saga has no cap. Mutually exclusive with <see cref="Duration"/>.</summary>
    public bool Unbounded { get; set; }
}
