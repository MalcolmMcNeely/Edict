using System.Reflection;

using Edict.Contracts.Commands;
using Edict.Core.Commands;
using Edict.Core.Idempotency;

using Orleans.Serialization;

namespace Edict.Core.Serialization;

// Orleans 10.2 enforces the type-manifest allow-list while building the silo
// grain manifest and while formatting command/result method types, not just on
// the deserialize path. Edict's grain interfaces are emitted by Edict's own
// generator and its contract surface is hand-MessagePack, so neither passes
// through Orleans' serializer code generator and neither is allow-listed by it.
// This filter opts the Edict-owned surface back in so the silo manifest builds
// and Edict calls round-trip; it scopes the allowance to types Edict itself
// defines or roots, never to arbitrary wire types.
sealed class EdictGrainTypeFilter : ITypeFilter
{
    static readonly Assembly CoreAssembly = typeof(IEdictCommandHandler).Assembly;
    static readonly Assembly ContractsAssembly = typeof(EdictCommand).Assembly;

    public bool? IsTypeAllowed(Type type) =>
        IsEdictOwned(type)
        || typeof(IEdictCommandHandler).IsAssignableFrom(type)
        || typeof(IEdictEventConsumer).IsAssignableFrom(type)
        || EdictSerialization.IsEdictContract(type)
            ? true
            : null;

    static bool IsEdictOwned(Type type)
    {
        var assembly = (type.IsGenericType ? type.GetGenericTypeDefinition() : type).Assembly;
        return assembly == CoreAssembly || assembly == ContractsAssembly;
    }
}
