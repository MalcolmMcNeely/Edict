using Orleans;

namespace Edict.Kafka.Wire;

/// <summary>
/// On-the-wire frame for one Kafka message produced by
/// <see cref="Internal.EdictKafkaAdapter"/>. Carries the Orleans
/// <c>StreamId</c> namespace/key plus the polymorphic event payload array.
/// Orleans' <c>Serializer</c> handles the <c>object[]</c> through its type
/// manifest, which routes <c>EdictEvent</c> subtypes through
/// <c>AddEdictContractSerializer</c> (ADR-0006/0007) — no .NET type names on
/// the wire.
/// </summary>
[GenerateSerializer]
public sealed class EdictKafkaWireEnvelope
{
    [Id(0)] public string StreamNamespace { get; set; } = "";
    [Id(1)] public string StreamKey { get; set; } = "";
    [Id(2)] public object[] Events { get; set; } = [];
}
