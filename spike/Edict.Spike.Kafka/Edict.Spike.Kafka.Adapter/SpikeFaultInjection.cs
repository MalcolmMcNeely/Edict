using System.Collections.Concurrent;

namespace Edict.Spike.Kafka.Adapter;

public static class SpikeFaultInjection
{
    static readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> s_entered = new();
    static readonly HashSet<Guid> s_hangIds = new();
    static readonly object s_gate = new();

    public static void ArmHang(Guid eventId)
    {
        lock (s_gate)
        {
            s_hangIds.Add(eventId);
        }
        s_entered[eventId] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static bool ShouldHang(Guid eventId)
    {
        lock (s_gate)
        {
            return s_hangIds.Contains(eventId);
        }
    }

    public static void SignalEntered(Guid eventId)
    {
        if (s_entered.TryGetValue(eventId, out var tcs))
        {
            tcs.TrySetResult(true);
        }
    }

    public static Task WaitEnteredAsync(Guid eventId) =>
        s_entered.TryGetValue(eventId, out var tcs) ? tcs.Task : Task.CompletedTask;

    public static Task HangForever() =>
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;

    public static void Reset()
    {
        lock (s_gate)
        {
            s_hangIds.Clear();
        }
        s_entered.Clear();
    }
}
