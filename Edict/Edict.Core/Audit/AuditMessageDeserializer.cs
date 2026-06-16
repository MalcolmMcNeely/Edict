using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using Orleans.Serialization;

namespace Edict.Core.Audit;

// Turns a captured audit body back into the concrete message the consumer authored.
// The body was written through the Orleans Serializer as the base EdictCommand or
// EdictEvent, so the concrete type identity travels in Orleans' manifest and
// deserializing back to the base yields the derived instance with no
// MessageType reflection. Pure — no Orleans grain, no I/O — so the dispatch and
// the failure shape are unit-testable in isolation.
static class AuditMessageDeserializer
{
    public static object Deserialize(Serializer serializer, byte[] body, EdictAuditRecord record)
    {
        try
        {
            return record.Kind switch
            {
                EdictAuditKind.Command => serializer.Deserialize<EdictCommand>(body),
                _ => serializer.Deserialize<EdictEvent>(body),
            };
        }
        catch (Exception exception)
        {
            throw new EdictAuditMessageDeserializationException(record.RecordId, record.MessageType, exception);
        }
    }
}
