using MessagePack;
using MessagePack.Resolvers;

namespace Edict.Spike.Kafka.Adapter;

internal static class SpikeKafkaWireFormat
{
    static readonly MessagePackSerializerOptions s_envelopeOptions = MessagePackSerializerOptions.Standard
        .WithCompression(MessagePackCompression.Lz4BlockArray);

    static readonly MessagePackSerializerOptions s_eventOptions = MessagePackSerializerOptions.Standard
        .WithResolver(TypelessContractlessStandardResolver.Instance)
        .WithCompression(MessagePackCompression.Lz4BlockArray);

    public static byte[] SerializeEvent(object evt) =>
        MessagePackSerializer.Serialize(typeof(object), evt, s_eventOptions);

    public static object DeserializeEvent(byte[] payload) =>
        MessagePackSerializer.Deserialize(typeof(object), payload, s_eventOptions)!;

    public static byte[] SerializeEnvelope(SpikeKafkaWireEnvelope envelope) =>
        MessagePackSerializer.Serialize(envelope, s_envelopeOptions);

    public static SpikeKafkaWireEnvelope DeserializeEnvelope(byte[] payload) =>
        MessagePackSerializer.Deserialize<SpikeKafkaWireEnvelope>(payload, s_envelopeOptions);
}
