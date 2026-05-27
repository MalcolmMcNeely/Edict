using Npgsql;

using Orleans;

namespace Edict.Postgres;

/// <summary>
/// Edict-serializable surrogate thrown when an <see cref="NpgsqlException"/>
/// fires inside an <c>Edict.Postgres</c> seam during a grain call.
/// <para>
/// Orleans has no codec for <see cref="NpgsqlException"/>; if one escapes a
/// grain method, the silo's outgoing message serializer faults on the way
/// back to the caller and the entire connection-processing pipeline crashes
/// (observed under saturation load — fire-and-forget at N=256 trips the
/// Npgsql connection pool, the pool exhaustion bubbles up as
/// <see cref="NpgsqlException"/>, and the silo dies). The provider catches
/// <see cref="NpgsqlException"/> at every seam that runs inside a grain call
/// and rethrows it as this type, which is annotated with
/// <c>[GenerateSerializer]</c> so Orleans owns a codec for it. The native
/// type and message are preserved verbatim as plain strings; the original
/// <see cref="NpgsqlException"/> is deliberately NOT carried as
/// <see cref="Exception.InnerException"/>, because Orleans walks the inner
/// chain during serialization and a still-attached
/// <see cref="NpgsqlException"/> would re-trigger the codec lookup that
/// motivated this type.
/// </para>
/// </summary>
[GenerateSerializer]
public sealed class EdictPostgresStorageException : Exception
{
    public EdictPostgresStorageException() { }

    public EdictPostgresStorageException(string message)
        : base(message) { }

    public EdictPostgresStorageException(string message, string nativeType, string nativeMessage)
        : base(message)
    {
        NativeType = nativeType;
        NativeMessage = nativeMessage;
    }

    /// <summary>Full type name of the <see cref="NpgsqlException"/> subclass that originally fired.</summary>
    [Id(0)]
    public string NativeType { get; set; } = string.Empty;

    /// <summary>Verbatim <see cref="Exception.Message"/> text of the originating <see cref="NpgsqlException"/>.</summary>
    [Id(1)]
    public string NativeMessage { get; set; } = string.Empty;

    internal static EdictPostgresStorageException From(NpgsqlException ex, string contextMessage) =>
        new(
            message: $"{contextMessage}: {ex.Message}",
            nativeType: ex.GetType().FullName ?? ex.GetType().Name,
            nativeMessage: ex.Message);
}
