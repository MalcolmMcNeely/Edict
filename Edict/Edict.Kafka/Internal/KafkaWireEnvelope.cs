using Orleans;

namespace Edict.Kafka.Internal;

// On-the-wire frame for one Kafka message produced by EdictKafkaAdapter.
// The [Alias] literal is frozen at the historical full type name so a future
// rename of this class does not change the Orleans serializer manifest a
// receiver matches against — wire format stays decoupled from C# identifier.
[GenerateSerializer]
[Alias("Edict.Kafka.Wire.EdictKafkaWireEnvelope")]
sealed class KafkaWireEnvelope
{
    [Id(0)] public string StreamNamespace { get; set; } = "";
    [Id(1)] public string StreamKey { get; set; } = "";
    [Id(2)] public object[] Events { get; set; } = [];
}
