namespace Edict.Substrate;

/// <summary>
/// Thrown when a substrate cannot be brought up — today on retry exhaustion,
/// later (from <c>ClusterHarness</c>) on a setup-deadline breach. Carries the
/// substrate name, the phase that failed, and the last underlying probe failure
/// so a stalled run names what to investigate. Deliberately not an
/// <c>Edict*</c>-typed runtime exception: that contract governs the
/// consumer-facing framework runtime, not the benchmark harness.
/// </summary>
public sealed class SubstrateBringUpFailedException : Exception
{
    public SubstrateBringUpFailedException(string substrateName, string phase, Exception? innerException)
        : base(BuildMessage(substrateName, phase), innerException)
    {
        SubstrateName = substrateName;
        Phase = phase;
    }

    public string SubstrateName { get; }

    public string Phase { get; }

    static string BuildMessage(string substrateName, string phase) =>
        $"Substrate '{substrateName}' failed during {phase}. On Podman/Windows this is typically a gvproxy/rootless port-forwarder stall after heavy container churn, which fresh containers usually clear. See the inner exception for the last underlying failure.";
}
