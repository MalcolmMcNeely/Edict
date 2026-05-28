namespace Edict.Generators.Tests;

internal sealed class GeneratorIncrementalityException : Xunit.Sdk.XunitException
{
    public GeneratorIncrementalityException(string message) : base(message)
    {
    }
}
