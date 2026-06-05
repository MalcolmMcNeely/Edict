namespace Edict.Core.DeadLetter;

// String-marker for a Command Handler schedule that hit its timeout cap with no
// OnScheduleTimeoutAsync hook. Never instantiated: the dead-letter promoter stamps
// its name onto the synthetic row's ExceptionType via nameof, never throwing it,
// because the timeout-promotion path sits on the safety net that cannot throw
// (a throw there would skip the schedule's Complete write and poison-loop the
// reminder). The name is the stable key operators alert on. Sibling to the
// instantiated EdictSagaTimeoutException; the divergence (marker vs. real throw)
// is deliberate.
sealed class EdictScheduleTimeoutException : Exception
{
    public EdictScheduleTimeoutException(string message)
        : base(message)
    {
    }
}
