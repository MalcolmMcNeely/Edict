using Edict.Contracts.Events;

namespace Edict.Testing.Internal;

/// <summary>
/// Per-subscriber K-counter held-event state machine. Pure: no Orleans, no DI,
/// no async. Models bounded reorder by deferring an arrival behind the K
/// subsequent arrivals to the same subscriber. Decrement counts <em>arrivals</em>,
/// not emissions — duplicates produced at release time do not double-tick the
/// counter. K-counter assertions are the load-bearing release mechanism;
/// <see cref="FlushAll"/> is the unconditional safety net the test harness's
/// drain settles against.
/// </summary>
sealed class HeldQueue
{
    readonly Dictionary<object, Queue<Held>> _bySubscriber = new();

    long _arrivalOrder;

    public int Count { get; private set; }

    public IReadOnlyList<EdictEvent> OnArrival(object subscriberKey, EdictEvent edictEvent, int holdCount)
    {
        var arrivalOrder = _arrivalOrder++;
        var released = DecrementAndCollect(subscriberKey);

        if (holdCount <= 0)
        {
            released.Add(edictEvent);
            return released;
        }

        if (!_bySubscriber.TryGetValue(subscriberKey, out var queue))
        {
            queue = new Queue<Held>();
            _bySubscriber[subscriberKey] = queue;
        }
        queue.Enqueue(new Held(edictEvent, holdCount, arrivalOrder));
        Count++;
        return released;
    }

    public IReadOnlyList<(object SubscriberKey, EdictEvent Event)> FlushAll()
    {
        var remaining = new List<(object, EdictEvent, long)>();
        foreach (var (key, queue) in _bySubscriber)
        {
            foreach (var held in queue)
            {
                remaining.Add((key, held.Event, held.ArrivalOrder));
            }
        }
        remaining.Sort((a, b) => a.Item3.CompareTo(b.Item3));

        _bySubscriber.Clear();
        Count = 0;

        return remaining
            .Select(t => (t.Item1, t.Item2))
            .ToList();
    }

    List<EdictEvent> DecrementAndCollect(object subscriberKey)
    {
        var released = new List<EdictEvent>();
        if (!_bySubscriber.TryGetValue(subscriberKey, out var queue))
        {
            return released;
        }

        var carry = new Queue<Held>();
        while (queue.Count > 0)
        {
            var held = queue.Dequeue();
            var ticked = held with { RemainingArrivals = held.RemainingArrivals - 1 };
            if (ticked.RemainingArrivals <= 0)
            {
                released.Add(ticked.Event);
                Count--;
            }
            else
            {
                carry.Enqueue(ticked);
            }
        }
        if (carry.Count == 0)
        {
            _bySubscriber.Remove(subscriberKey);
        }
        else
        {
            _bySubscriber[subscriberKey] = carry;
        }
        return released;
    }

    readonly record struct Held(EdictEvent Event, int RemainingArrivals, long ArrivalOrder);
}
