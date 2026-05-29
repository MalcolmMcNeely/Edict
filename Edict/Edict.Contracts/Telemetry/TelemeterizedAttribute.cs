namespace Edict.Contracts.Telemetry;

/// <summary>
/// Marks a primitive property of a <see cref="Commands.EdictCommand"/> or
/// <c>EdictEvent</c> subclass for automatic OTEL tag emission. A source
/// generator writes the property value as
/// <c>edict.{snake_case_property_name}</c> on the active span — the same
/// concept resolves to the same key across declaring types so cross-type
/// queries work without OR-joining. For Commands the tag lands on
/// the <c>edict.command</c> dispatch span; for Events on both
/// <c>edict.event.publish</c> and <c>edict.event.handle</c>. Placing this
/// attribute on a non-primitive property is a compile error (EDICT005).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EdictTelemeterizedAttribute : Attribute;
