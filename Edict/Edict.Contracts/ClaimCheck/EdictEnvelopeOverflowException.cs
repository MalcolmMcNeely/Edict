namespace Edict.Contracts.ClaimCheck;

/// <summary>
/// Thrown at the Outbox commit boundary when the envelope-wrapped bytes
/// of a single event still exceed the storage per-property cap after the
/// claim-check threshold has been applied (ADR 0024). Carries the route
/// key, event type, and measured byte length so the operator sees a
/// designed framework failure instead of an Azure Table 400 surfaced
/// from deep inside Orleans's storage layer.
/// </summary>
public sealed class EdictEnvelopeOverflowException : Exception
{
    public EdictEnvelopeOverflowException(Guid routeKey, string eventType, int measuredBytes)
        : base(BuildMessage(routeKey, eventType, measuredBytes))
    {
        RouteKey = routeKey;
        EventType = eventType;
        MeasuredBytes = measuredBytes;
    }

    /// <summary>Route key of the aggregate whose Handle raised the oversized event.</summary>
    public Guid RouteKey { get; }

    /// <summary>Full type name of the raised event that overflowed.</summary>
    public string EventType { get; }

    /// <summary>Post-wrap byte length the commit pipeline measured before throwing.</summary>
    public int MeasuredBytes { get; }

    static string BuildMessage(Guid routeKey, string eventType, int measuredBytes) =>
        $"Event '{eventType}' on route '{routeKey}' wrapped to {measuredBytes} bytes, exceeding the storage per-property cap. "
        + "Raise EdictAzureOptions.ClaimCheckThresholdBytes headroom or shrink the event.";
}
