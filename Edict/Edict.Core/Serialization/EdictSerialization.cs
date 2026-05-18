using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Results;

using Orleans.Serialization;

namespace Edict.Core.Serialization;

/// <summary>
/// Orleans serialization for the <c>Edict.Contracts</c> contract surface.
/// <para>
/// <c>Command</c>/<c>CommandResult</c> live in an Orleans-free assembly (ADR
/// 0005), and a generated <c>[GenerateSerializer]</c> surrogate is impossible:
/// Orleans' serializer source generator runs as a sibling of Edict's
/// generator and never observes its output (ADR 0006's prohibition, still
/// load-bearing). MessagePack attributes are hand-written in source — on the
/// bases by Edict, on concrete commands by the consumer — so they never enter
/// that generator-ordering trap. Orleans' external-serializer integration
/// writes the concrete type identity via its own type manifest and delegates
/// only the body to MessagePack, so the abstract <c>Command</c> parameter on
/// <c>IEdictSender.Send</c> round-trips polymorphically with no <c>[Union]</c>
/// on the base (ADR 0007).
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

    private static bool IsEdictContract(Type type) =>
        typeof(Command).IsAssignableFrom(type)
        || typeof(CommandResult).IsAssignableFrom(type)
        || type == typeof(RejectionReason)
        || typeof(Event).IsAssignableFrom(type);
}
