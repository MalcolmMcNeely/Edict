namespace Edict.Contracts.Routing;

/// <summary>
/// Generator-applied annotation that points <c>AddEdict()</c> at the
/// per-assembly event tag-writer registrar. One attribute per consumer
/// assembly that contains at least one concrete <c>EdictEvent</c> with at
/// least one <c>[EdictTelemeterized]</c> property; the referenced static type
/// exposes a <c>Register(Dictionary&lt;Type, Action&lt;EdictEvent, Activity&gt;&gt;)</c>
/// method that contributes this assembly's generator-emitted tag writers to
/// the runtime <c>IEventTagWriters</c> map. Brand-prefixed because the
/// generator emits it on the consumer's behalf — clause (a) of CONTEXT.md's
/// brand rule.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EdictEventTagWritersAttribute(Type registrarType) : Attribute
{
    public Type RegistrarType { get; } = registrarType;
}
