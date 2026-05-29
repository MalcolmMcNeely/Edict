namespace Edict.Core.DeadLetter;

/// <summary>
/// Runtime fault for the cause narrative "consumer raised something from an
/// unscanned assembly". Thrown from <c>EventStreamAccessors.Resolve</c>,
/// <c>RowTypeResolver.Resolve</c>, and the Kafka stream-to-topic resolver
/// when a concrete event / row alias / stream is missing from the framework's
/// generator-emitted registry. <see cref="Kind"/> discriminates the lookup
/// surface so the operator can fix the right registration on a single typed
/// catch. Maps to the <c>Wiring</c> failure-reason bucket via
/// <see cref="DeadLetterFailureClassifier"/>.
/// </summary>
public sealed class EdictUnregisteredTypeException : Exception
{
    public EdictUnregisteredTypeException(Kind kind, string typeName, string message)
        : base(message)
    {
        UnregisteredKind = kind;
        TypeName = typeName;
    }

    public EdictUnregisteredTypeException(Kind kind, string typeName, string message, Exception innerException)
        : base(message, innerException)
    {
        UnregisteredKind = kind;
        TypeName = typeName;
    }

    /// <summary>Which registry the missing lookup hit (event accessor map, row-alias
    /// converter, or stream-to-topic map).</summary>
    public Kind UnregisteredKind { get; }

    /// <summary>The CLR full-name or alias the lookup was for. Operator-facing — the
    /// fix is to scan the declaring assembly via <c>AddEdict()</c>.</summary>
    public string TypeName { get; }

    public enum Kind
    {
        Event,
        RowAlias,
        Stream,
    }
}
