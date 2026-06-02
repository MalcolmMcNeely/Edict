namespace Edict.Substrate;

/// <summary>
/// Retries a substrate bring-up whose host-readiness probe can fail under
/// Podman/Windows. After rapid container churn the gvproxy (rootless) port
/// forwarder can stall: the container is healthy and reachable inside the VM,
/// but its host-mapped port never opens, so the probe times out. A stalled
/// mapping does not recover on the same container — only a freshly created
/// container gets a clean forward — so each retry must dispose its containers
/// and build new ones. The <paramref name="attempt"/> owns that
/// create-probe-dispose-on-failure cycle; this helper spaces the attempts and
/// surfaces the last failure once they are exhausted.
/// </summary>
public static class SubstrateBringUp
{
    public static async Task<ISubstrateRuntime> WithRetryAsync(
        string substrateName,
        Func<CancellationToken, Task<ISubstrateRuntime>> attempt,
        CancellationToken cancellationToken,
        int maxAttempts = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(substrateName);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        Exception? lastError = null;
        for (var attemptNumber = 1; attemptNumber <= maxAttempts; attemptNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await attempt(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                if (attemptNumber == maxAttempts)
                {
                    break;
                }
                // Backoff grows with the attempt so a forwarder mid-churn gets a
                // moment to drain before the next fresh container claims a port.
                var backoff = TimeSpan.FromSeconds(2 * attemptNumber);
                Console.Error.WriteLine(
                    $"[{substrateName}] bring-up attempt {attemptNumber}/{maxAttempts} failed ({exception.GetType().Name}: {exception.Message}). Recreating containers in {backoff.TotalSeconds:F0}s and retrying.");
                await Task.Delay(backoff, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Substrate '{substrateName}' failed to come up after {maxAttempts} attempts. The final attempt's containers could not pass their host-readiness probe — on Podman/Windows this is typically a gvproxy/rootless port-forwarder stall after heavy container churn, which fresh containers usually clear. See the inner exception for the last probe failure.",
            lastError);
    }
}
