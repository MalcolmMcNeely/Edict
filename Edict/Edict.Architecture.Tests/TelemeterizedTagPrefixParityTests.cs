using System.Reflection;

using Edict.Generators;
using Edict.Telemetry;

using Xunit;

namespace Edict.Architecture.Tests;

/// <summary>
/// Generators can't reference <c>Edict.Telemetry</c> (they target netstandard2.0
/// and run at compile time), so the Telemeterized tag prefix is duplicated as an
/// <c>internal const string</c> in <c>EdictWellKnownNames</c>. This fact asserts
/// the two sources of truth agree so a future rename of one can't silently
/// emit one prefix from the generator and assert another at runtime.
/// </summary>
public class TelemeterizedTagPrefixParityTests
{
    [Fact]
    public void GeneratorAndRuntimePrefixes_ShouldHaveTheSameValue()
    {
        var generatorPrefix = (string?)typeof(EdictGenerator).Assembly
            .GetType("Edict.Generators.EdictWellKnownNames", throwOnError: true)!
            .GetField("TelemeterizedTagPrefix", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue();

        Assert.Equal(SemanticConventions.Telemeterized.Prefix, generatorPrefix);
    }
}
