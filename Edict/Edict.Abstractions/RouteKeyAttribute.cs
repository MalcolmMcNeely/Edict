namespace Edict.Abstractions;

/// <summary>
/// Marks the single <see cref="Guid"/> property of a concrete
/// <see cref="Command"/> that routes it to its aggregate grain. Exactly one
/// per command; that Guid becomes the grain key and, when the handler raises
/// events, the event stream's <c>sourceAggregateGuid</c> — one correlation id
/// flowing command → grain → event → handler. Named <c>RouteKey</c> rather
/// than <c>Key</c> to avoid colliding with
/// <c>System.ComponentModel.DataAnnotations.KeyAttribute</c> (ADR 0004).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RouteKeyAttribute : Attribute;
