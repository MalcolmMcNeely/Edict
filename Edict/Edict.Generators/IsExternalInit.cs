// netstandard2.0 lacks IsExternalInit, which the C# compiler requires for
// `init` accessors and positional records. This internal polyfill enables the
// modern record syntax used by the generator's internal models.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
