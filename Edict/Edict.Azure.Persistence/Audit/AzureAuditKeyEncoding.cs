using System.Text;

namespace Edict.Azure.Persistence.Audit;

// Encodes an arbitrary string into an Azure Table partition/row key. A principal is
// an opaque consumer string (an oid, a realm slug, an employee id) and an entity key
// is a consumer grain key, so either can carry the characters Azure forbids in a key
// (slash, backslash, hash, question mark, control chars) or exceed the key length
// cap. URL-safe Base64 of the UTF-8 bytes is collision-free and key-legal, so two
// distinct values never share a partition. The read path never decodes — the full
// record body in each row is the source of truth — so the encoding only has to be
// deterministic and reversible-free, not human-legible.
static class AzureAuditKeyEncoding
{
    public static string Encode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
