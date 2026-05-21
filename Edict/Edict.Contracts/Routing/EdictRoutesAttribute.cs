namespace Edict.Contracts.Routing;

/// <summary>
/// Generator-applied annotation that points <c>AddEdict()</c> at the
/// per-assembly route registrar. One attribute per consumer
/// assembly that contains at least one <c>EdictCommandHandler</c>; the
/// referenced static type exposes a <c>Register(Dictionary&lt;Type,
/// CommandRoute&gt;)</c> method that contributes this assembly's routes to
/// the runtime route map. Brand-prefixed because the generator emits it on
/// the consumer's behalf — clause (a) of CONTEXT.md's brand rule, the first
/// precedent for a brand-prefixed attribute that no consumer writes.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EdictRoutesAttribute(Type registrarType) : Attribute
{
    public Type RegistrarType { get; } = registrarType;
}
