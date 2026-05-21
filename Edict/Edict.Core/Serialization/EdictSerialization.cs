using Edict.Contracts;
using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Orleans.Serialization;

namespace Edict.Core.Serialization;

/// <summary>
/// Orleans serialization for the <c>Edict.Contracts</c> contract surface.
/// <para>
/// <c>EdictCommand</c>/<c>EdictCommandResult</c> live in an Orleans-free
/// assembly, and a generated <c>[GenerateSerializer]</c> surrogate is
/// impossible: Orleans' serializer source generator runs as a sibling of
/// Edict's generator and never observes its output (a still-load-bearing
/// prohibition). MessagePack attributes are hand-written in source — on the
/// bases by Edict, on concrete commands by the consumer — so they never enter
/// that generator-ordering trap. Orleans' external-serializer integration
/// writes the concrete type identity via its own type manifest and delegates
/// only the body to MessagePack, so the abstract <c>EdictCommand</c> parameter on
/// <c>IEdictSender.Send</c> round-trips polymorphically with no <c>[Union]</c>
/// on the base.
/// </para>
/// </summary>
public static class EdictSerialization
{
    /// <summary>
    /// Routes the Edict command/result contract types through Orleans'
    /// MessagePack serializer. Apply on every silo and client that carries
    /// Edict traffic.
    /// </summary>
    public static ISerializerBuilder AddEdictContractSerializer(this ISerializerBuilder builder) =>
        builder.AddMessagePackSerializer(IsEdictContract);

    static bool IsEdictContract(Type type) =>
        typeof(EdictCommand).IsAssignableFrom(type)
        || typeof(EdictCommandResult).IsAssignableFrom(type)
        || type == typeof(EdictRejectionReason)
        || type == typeof(EdictUnit)
        || typeof(EdictEvent).IsAssignableFrom(type);
}
