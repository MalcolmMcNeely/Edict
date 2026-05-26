using MessagePack;

namespace Edict.Spike.Kafka.Adapter;

[MessagePackObject]
public sealed class SpikeKafkaWireEnvelope
{
    [Key(0)] public string StreamNamespace { get; set; } = string.Empty;
    [Key(1)] public string StreamKey { get; set; } = string.Empty;
    [Key(2)] public byte[][] EventPayloads { get; set; } = Array.Empty<byte[]>();
    [Key(3)] public Dictionary<string, object>? RequestContext { get; set; }
}
