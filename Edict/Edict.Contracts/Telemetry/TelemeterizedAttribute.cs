namespace Edict.Contracts.Telemetry;

/// <summary>
/// Marks a primitive property of a <see cref="Commands.EdictCommand"/> or <c>EdictEvent</c>
/// subclass for automatic OTEL tag emission. A source generator writes the
/// property value as <c>edict.{type}.{property}</c> on the active span.
/// Placing this attribute on a non-primitive property is a compile error.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EdictTelemeterizedAttribute : Attribute;
