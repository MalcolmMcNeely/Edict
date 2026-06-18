namespace Edict.Substrate;

/// <summary>
/// One within-boot bring-up step: create, start, and probe one container (or
/// container set), returning the live resource for the policy to own. A step
/// that fails must clean up its own partial resources before throwing — the
/// policy disposes only the resources steps hand back. The <paramref name="cancellationToken"/>
/// the policy passes is cancelled when the step exceeds its per-attempt budget,
/// so a step's own waits should honour it.
/// </summary>
public delegate Task<IAsyncDisposable> SubstrateBringUpStep(BringUpTuning tuning, CancellationToken cancellationToken);

/// <summary>
/// Brings a substrate up from ordered steps, owning the retry/stagger/timeout
/// machinery so each substrate is a list of create-start-probe steps plus an
/// assembly callback — never a re-derivation of the resilience logic.
/// Encapsulates: retry with dispose-and-recreate between attempts (a stalled
/// host-port mapping never clears on the same container, so each attempt starts
/// fresh), within-boot step sequencing with a stagger gap, a per-attempt
/// wait/probe timeout, and an exponential-ish backoff. All timing reads from the
/// injected <see cref="TimeProvider"/>, and steps run through a delegate seam, so
/// the policy is exercised by clock-free tests with no real Docker. Cross-boot
/// settle and the setup-spanning deadline are the harness's concern, not this
/// module's.
/// </summary>
public sealed class SubstrateBringUpPolicy
{
    static readonly TimeSpan RetryBackoffBase = TimeSpan.FromSeconds(2);

    const string ContainerBringUpPhase = "container bring-up";

    readonly TimeProvider _timeProvider;

    public SubstrateBringUpPolicy(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public async Task<ISubstrateRuntime> BringUpAsync(
        string substrateName,
        IReadOnlyList<SubstrateBringUpStep> steps,
        Func<IReadOnlyList<IAsyncDisposable>, ISubstrateRuntime> assemble,
        BringUpTuning tuning,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(substrateName);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(assemble);
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentOutOfRangeException.ThrowIfZero(steps.Count);

        Exception? lastError = null;
        for (var attempt = 1; attempt <= tuning.RetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await RunAttemptAsync(substrateName, steps, assemble, tuning, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                if (attempt == tuning.RetryCount)
                {
                    break;
                }
                // Backoff grows with the attempt so a forwarder mid-churn gets a
                // moment to drain before the next fresh container claims a port.
                await Task.Delay(RetryBackoffBase * attempt, _timeProvider, cancellationToken);
            }
        }

        throw new SubstrateBringUpFailedException(substrateName, ContainerBringUpPhase, lastError);
    }

    async Task<ISubstrateRuntime> RunAttemptAsync(
        string substrateName,
        IReadOnlyList<SubstrateBringUpStep> steps,
        Func<IReadOnlyList<IAsyncDisposable>, ISubstrateRuntime> assemble,
        BringUpTuning tuning,
        CancellationToken cancellationToken)
    {
        var started = new List<IAsyncDisposable>(steps.Count);
        // The Testcontainers wait and the host-readiness probe are bounded inside
        // each step; this outer budget is the backstop that turns a step ignoring
        // both into a failed attempt rather than a hang.
        var perStepTimeout = tuning.TestcontainersWaitTimeout + tuning.HostReadinessProbeDeadline;
        try
        {
            for (var stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                if (stepIndex > 0)
                {
                    await Task.Delay(tuning.WithinBootStaggerGap, _timeProvider, cancellationToken);
                }

                started.Add(await RunStepAsync(substrateName, steps[stepIndex], stepIndex, tuning, perStepTimeout, cancellationToken));
            }

            return assemble(started);
        }
        catch
        {
            // A doomed attempt must not leak the containers it already started:
            // dispose in reverse so dependents tear down before their dependencies.
            for (var disposeIndex = started.Count - 1; disposeIndex >= 0; disposeIndex--)
            {
                await DisposeQuietlyAsync(started[disposeIndex]);
            }
            throw;
        }
    }

    async Task<IAsyncDisposable> RunStepAsync(
        string substrateName,
        SubstrateBringUpStep step,
        int stepIndex,
        BringUpTuning tuning,
        TimeSpan perStepTimeout,
        CancellationToken cancellationToken)
    {
        using var stepCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var budgetTimer = _timeProvider.CreateTimer(
            static state => ((CancellationTokenSource)state!).Cancel(),
            stepCancellation,
            perStepTimeout,
            Timeout.InfiniteTimeSpan);
        try
        {
            return await step(tuning, stepCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller did not cancel, so the budget timer did: a step that
            // overran its wait/probe budget fails this attempt (and retries with
            // a fresh container), rather than propagating as a real cancellation.
            throw new TimeoutException(
                $"Substrate '{substrateName}' step {stepIndex} exceeded its {perStepTimeout.TotalSeconds:F0} s per-attempt wait/probe budget.");
        }
    }

    static async Task DisposeQuietlyAsync(IAsyncDisposable resource)
    {
        try
        {
            await resource.DisposeAsync();
        }
        catch
        {
            // A teardown failure must not mask the bring-up failure that triggered it.
        }
    }
}
