using System.Collections.Concurrent;

using Orleans.Serialization.TypeSystem;

namespace Edict.Core.Outbox;

// Resolves an UpsertRowEffect.RowAlias to its concrete CLR Type via the Orleans
// TypeConverter, caching the lookup so the drain hot path doesn't pay the
// manifest walk on every event for the same row type.
sealed class RowTypeResolver(TypeConverter typeConverter)
{
    readonly ConcurrentDictionary<string, Type> _cache = new();

    public Type Resolve(string alias)
    {
        if (_cache.TryGetValue(alias, out var cached))
        {
            return cached;
        }

        var resolved = typeConverter.Parse(alias)
            ?? throw new InvalidOperationException(
                $"UpsertRow drain could not resolve row alias '{alias}' to a CLR type. " +
                "The consumer's [Alias]-decorated row POCO must be present in a loaded assembly.");

        return _cache.GetOrAdd(alias, resolved);
    }
}
