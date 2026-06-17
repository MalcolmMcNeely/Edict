using System.Buffers;
using System.Buffers.Binary;
using System.Text;

using Edict.Contracts.Audit;

namespace Edict.Core.Audit;

// The stable, framework-defined byte encoding of an audit record's content, over
// which the chain hash is computed. Deliberately NOT MessagePack: a serialization
// version bump must never change a record's hash, so verification re-derives these
// bytes from the stored fields with a frozen, hand-rolled layout instead of
// re-serializing the object. The two hash fields (PreviousHash, RecordHash) are
// excluded — PreviousHash is folded in by HashChain.Advance, RecordHash is the
// output. Field order and widths here are a wire contract: changing them breaks
// every existing chain, so they only ever grow by appending.
static class AuditRecordCanonicalForm
{
    public static byte[] Encode(EdictAuditRecord record)
    {
        var buffer = new ArrayBufferWriter<byte>();
        WriteGuid(buffer, record.RecordId);
        WriteInt32(buffer, (int)record.Kind);
        WriteInt32(buffer, record.Outcome is { } outcome ? (int)outcome : -1);

        WriteInt32(buffer, record.RejectionReasons.Count);
        foreach (var reason in record.RejectionReasons)
        {
            WriteString(buffer, reason.Code);
            WriteString(buffer, reason.Message);
        }

        WriteString(buffer, record.Principal?.Value);
        WriteGuid(buffer, record.CorrelationId);
        WriteString(buffer, record.EntityType);
        WriteString(buffer, record.EntityKey);
        WriteString(buffer, record.MessageType);
        WriteInt64(buffer, record.OccurredAt.UtcTicks);
        WriteInt64(buffer, record.Sequence);
        WriteBytes(buffer, record.PayloadHash);
        WriteString(buffer, record.Tenant?.Value);

        return buffer.WrittenSpan.ToArray();
    }

    static void WriteGuid(ArrayBufferWriter<byte> buffer, Guid value)
    {
        var span = buffer.GetSpan(16);
        value.TryWriteBytes(span);
        buffer.Advance(16);
    }

    static void WriteInt32(ArrayBufferWriter<byte> buffer, int value)
    {
        var span = buffer.GetSpan(4);
        BinaryPrimitives.WriteInt32LittleEndian(span, value);
        buffer.Advance(4);
    }

    static void WriteInt64(ArrayBufferWriter<byte> buffer, long value)
    {
        var span = buffer.GetSpan(8);
        BinaryPrimitives.WriteInt64LittleEndian(span, value);
        buffer.Advance(8);
    }

    // Length-prefixed so two adjacent fields can never alias (a -1 prefix marks a
    // null, distinct from an empty string's 0 prefix).
    static void WriteString(ArrayBufferWriter<byte> buffer, string? value)
    {
        if (value is null)
        {
            WriteInt32(buffer, -1);
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(buffer, byteCount);
        var span = buffer.GetSpan(byteCount);
        Encoding.UTF8.GetBytes(value, span);
        buffer.Advance(byteCount);
    }

    static void WriteBytes(ArrayBufferWriter<byte> buffer, byte[] value)
    {
        WriteInt32(buffer, value.Length);
        buffer.Write(value);
    }
}
