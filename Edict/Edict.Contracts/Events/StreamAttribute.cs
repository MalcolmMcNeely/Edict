namespace Edict.Contracts.Events;

/// <summary>
/// Names the domain stream a concrete <see cref="EdictEvent"/> belongs to. Required
/// on every concrete event — an analyzer errors if it is absent. Both the
/// publisher's flush target and the subscriber's implicit subscription are
/// derived from this name, so a missing attribute would silently misroute
/// events rather than failing at compile time (ADR 0011).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class EdictStreamAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
