namespace Edict.Contracts.Audit;

// One captured body bound to the audit record that attests to it: the record id is
// the store key, the bytes are the exact serialized message the record's PayloadHash
// was computed over. A transient drain-pass parameter, never persisted or sent over
// the wire (the durable copy rides the AuditChain slice as serialized bytes until
// drained), so it needs no serializer attributes.
internal readonly record struct EdictAuditPayload(Guid RecordId, ReadOnlyMemory<byte> Body);
