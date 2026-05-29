using System.Diagnostics;

using Edict.Contracts.Events;

namespace Edict.Telemetry;

/// <summary>
/// Resolves the generator-emitted <c>[EdictTelemeterized]</c> tag writer for a
/// concrete <see cref="EdictEvent"/>. Mirror of
/// <c>Edict.Core.Outbox.IEventStreamAccessors</c>: the dictionary is populated
/// at <c>AddEdict()</c> time from per-assembly registrars, then frozen.
/// </summary>
public interface IEventTagWriters
{
    /// <summary>
    /// Returns the writer for <paramref name="eventType"/>. Events with no
    /// <c>[EdictTelemeterized]</c> properties have no registration — callers
    /// MUST skip the invocation when this returns <c>false</c> so the common
    /// path stays free.
    /// </summary>
    bool TryGet(Type eventType, out Action<EdictEvent, Activity> writer);
}
