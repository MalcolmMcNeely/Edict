using Edict.Abstractions;

using Orleans.Serialization;

namespace Edict.Core.Serialization;

/// <summary>
/// Orleans serialization for the <c>Edict.Abstractions</c> contract surface.
/// <para>
/// <c>Command</c>/<c>CommandResult</c> live in an Orleans-free assembly (ADR
/// 0005), and a generated <c>[GenerateSerializer]</c> surrogate is impossible:
/// Orleans' serializer source generator runs as a sibling of Edict's
/// generator and never observes its output, so no codec is ever emitted for
/// generated surrogate types. The robust, codegen-independent choice is to
/// serialize the closed contract hierarchy with Orleans' JSON serializer,
/// configured here so it applies on both the silo and the client.
/// </para>
/// </summary>
public static class EdictSerialization
{
    /// <summary>
    /// Routes the Edict command/result contract types through Orleans' JSON
    /// serializer. Apply on every silo and client that carries Edict traffic.
    /// </summary>
    public static ISerializerBuilder AddEdictContractSerializer(this ISerializerBuilder builder) =>
        builder.AddJsonSerializer(IsEdictContract);

    private static bool IsEdictContract(Type type) =>
        typeof(Command).IsAssignableFrom(type)
        || typeof(CommandResult).IsAssignableFrom(type)
        || type == typeof(RejectionReason);
}
