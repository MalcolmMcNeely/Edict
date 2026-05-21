namespace Edict.Testing.Internal;

/// <summary>
/// Seeded chaos-roll wrapper. Two independent <see cref="Random"/> streams —
/// one for duplicate redelivery, one for reorder — so tuning either probability
/// keeps the other's sequence stable. The lone <see cref="ChaosOptions.Seed"/>
/// is the only knob that re-baselines tests; the reorder stream is derived as
/// <c>Seed ^ 0x5_EE_D5</c>.
/// </summary>
sealed class ChaosRoller(ChaosOptions chaos)
{
    readonly Random _duplicateRng = new(chaos.Seed);
    readonly Random _reorderRng = new(chaos.Seed ^ 0x5_EE_D5);
    readonly Lock _duplicateLock = new();
    readonly Lock _reorderLock = new();

    public int ExtraDeliveries(Type grainClass)
    {
        if (chaos.MaxExtraDeliveries <= 0)
        {
            return 0;
        }

        if (SubscriberMap.IsEventHandler(grainClass) && !chaos.InvocationsEnabled)
        {
            return 0;
        }

        lock (_duplicateLock)
        {
            return _duplicateRng.NextDouble() < chaos.DuplicateProbability
                ? _duplicateRng.Next(1, chaos.MaxExtraDeliveries + 1)
                : 0;
        }
    }

    public (bool Hold, int HoldDistance) ShouldHold(Type grainClass)
    {
        if (chaos.MaxReorderDistance <= 0 || chaos.ReorderProbability <= 0)
        {
            return (false, 0);
        }

        lock (_reorderLock)
        {
            if (_reorderRng.NextDouble() < chaos.ReorderProbability)
            {
                return (true, _reorderRng.Next(1, chaos.MaxReorderDistance + 1));
            }
            return (false, 0);
        }
    }
}
