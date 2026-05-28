namespace Edict.Contracts.Routing;

/// <summary>
/// Generator-applied annotation that points <c>AddEdict()</c> at the
/// per-assembly event-stream accessor registrar. One attribute per consumer
/// assembly that contains at least one concrete <c>EdictEvent</c>; the
/// referenced static type exposes a <c>Register(Dictionary&lt;Type,
/// EdictEventStreamAccessor&gt;)</c> method that contributes this assembly's
/// generator-emitted accessor entries to the runtime
/// <c>IEventStreamAccessors</c> map. Brand-prefixed because the generator emits
/// it on the consumer's behalf — clause (a) of CONTEXT.md's brand rule.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EdictEventStreamsAttribute(Type registrarType) : Attribute
{
    public Type RegistrarType { get; } = registrarType;
}
