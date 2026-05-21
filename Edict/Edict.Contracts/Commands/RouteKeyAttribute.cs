namespace Edict.Contracts.Commands;

/// <summary>
/// Marks the single <see cref="Guid"/> property of a concrete
/// <see cref="EdictCommand"/> that routes it to its aggregate grain. Exactly one
/// per command; that Guid becomes the grain key and, when the handler raises
/// events, the event stream's <c>sourceAggregateGuid</c> — one correlation id
/// flowing command → grain → event → handler. Named <c>RouteKey</c> rather
/// than <c>Key</c> to avoid colliding with
/// <c>System.ComponentModel.DataAnnotations.KeyAttribute</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EdictRouteKeyAttribute : Attribute;
