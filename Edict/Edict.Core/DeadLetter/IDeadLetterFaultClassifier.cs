namespace Edict.Core.DeadLetter;

// First-party provider seam, deliberately internal: a persistence or streaming
// provider recognises its own driver faults by real exception type and maps
// them to an existing failure-reason bucket, so Core never needs a reference
// to a driver package. Registered classifiers are consulted only where the
// built-in switch defers (Unhandled), never to override a framework cause.
interface IDeadLetterFaultClassifier
{
    // Returns an allow-listed FailureReasonValues constant, or null to defer
    // to the next classifier. Implementations must not throw (the composition
    // wraps each call and treats a throw as a defer), but a defer is the
    // contract, not a fallback.
    string? Classify(Exception exception);
}
