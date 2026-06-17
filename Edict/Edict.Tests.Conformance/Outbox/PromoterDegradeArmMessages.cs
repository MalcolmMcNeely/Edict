using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using MessagePack;

namespace Edict.Tests.Conformance.Outbox;

// An event whose forensic JSON body cannot be materialised: the Explode getter
// throws, but [IgnoreMember] keeps it off the MessagePack wire so the event
// round-trips through the Orleans serializer intact. Only the promoter's
// JsonSerializer.Serialize pass over the materialised body invokes the getter,
// modelling a consumer payload that defeats forensic JSON rendering and so
// drives the serialization-failure degrade arm through a real drain.
[EdictStream("PromoterDegradeUnserialisable")]
public sealed partial record UnserialisableForensicBodyEvent : EdictEvent
{
    [EdictRouteKey]
    public Guid RouteKey { get; init; }

    [IgnoreMember]
    public string Explode => throw new InvalidOperationException("forensic body getter blew up during promotion");
}

// A command with no [EdictRouteKey], staged as a SendCommand outbox entry so the
// promoter's BuildFromSendCommand cannot resolve a route key and falls to the
// missing-route-key degrade arm. [Alias] + keyAsPropertyName MessagePack is the
// minimal serializable shape for a fabricated route-key-less command;
// [GenerateSerializer] crashes on it, so the analyzer that would demand the
// route key is suppressed instead.
#pragma warning disable EDICT003, EDICT006
[Alias("Edict.Tests.Conformance.Outbox.MissingRouteKeyCommand")]
[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record MissingRouteKeyCommand : EdictCommand
{
    public Guid Id { get; init; }
}
#pragma warning restore EDICT003, EDICT006
