namespace Edict.Mcp.Handlers;

enum SagaTimeoutCapKind
{
    Duration,
    Unbounded,
    Default,
}

sealed record SagaTimeoutCap(SagaTimeoutCapKind Kind, string? Duration)
{
    public static readonly SagaTimeoutCap InheritsDefault = new(SagaTimeoutCapKind.Default, Duration: null);
    public static readonly SagaTimeoutCap Unbounded = new(SagaTimeoutCapKind.Unbounded, Duration: null);

    public static SagaTimeoutCap ForDuration(string duration) => new(SagaTimeoutCapKind.Duration, duration);
}
