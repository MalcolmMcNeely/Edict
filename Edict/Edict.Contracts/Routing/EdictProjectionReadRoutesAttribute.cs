namespace Edict.Contracts.Routing;

/// <summary>
/// Generator-applied annotation that points <c>AddEdict()</c> at the per-assembly
/// projection-read registrar. One attribute per consumer assembly that contains
/// at least one <c>EdictListProjectionBuilder&lt;TRow&gt;</c>; the referenced
/// static type exposes a <c>Register(Dictionary&lt;Type, string&gt;)</c> method
/// that maps each read-model row type to its owning projection grain class name,
/// so the read facade can address the grain. Mirrors
/// <see cref="EdictRoutesAttribute"/> on the command side.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EdictProjectionReadRoutesAttribute(Type registrarType) : Attribute
{
    public Type RegistrarType { get; } = registrarType;
}
