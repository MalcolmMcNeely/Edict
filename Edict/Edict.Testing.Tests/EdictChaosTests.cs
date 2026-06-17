using System.Globalization;

using Edict.Testing.Internal;

using Xunit;

namespace Edict.Testing.Tests;

/// <summary>
/// Isolation tests for the process-wide chaos-seed resolver. Each test owns the
/// <c>EDICT_CHAOS_SEED</c> environment variable and the memoised seed for the
/// duration of its body via <see cref="ChaosSeedScope"/>, restoring both on exit,
/// so the run-wide seed every other harness test reads is left untouched. The
/// whole collection is serialized (see <see cref="ChaosSeedCollection"/>) so no
/// parallel test reads the seed mid-reset.
/// </summary>
[Collection(ChaosSeedCollection.Name)]
public sealed class EdictChaosTests
{
    [Fact]
    public void CurrentSeed_ResolvesDecimalEnvironmentValue()
    {
        // Arrange
        using var scope = new ChaosSeedScope("970695");

        // Act
        var seed = EdictChaos.CurrentSeed;

        // Assert
        Assert.Equal(970695, seed);
    }

    [Fact]
    public void CurrentSeed_ResolvesHexEnvironmentValue()
    {
        // Arrange
        using var scope = new ChaosSeedScope("0xED1C7");

        // Act
        var seed = EdictChaos.CurrentSeed;

        // Assert
        Assert.Equal(0xED1C7, seed);
    }

    [Fact]
    public void CurrentSeed_IsStableAcrossReads_WhenEnvironmentUnset()
    {
        // Arrange
        using var scope = new ChaosSeedScope(rawValue: null);

        // Act
        var first = EdictChaos.CurrentSeed;
        var second = EdictChaos.CurrentSeed;

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void ChaosOptionsDefault_DerivesSeedFromCurrentSeed()
    {
        // Arrange
        using var scope = new ChaosSeedScope(12345);

        // Act
        var options = ChaosOptions.Default;

        // Assert
        Assert.Equal(EdictChaos.CurrentSeed, options.Seed);
        Assert.Equal(12345, options.Seed);
    }
}

/// <summary>
/// Pins <c>EDICT_CHAOS_SEED</c> to <paramref name="rawValue"/> (or clears it when
/// null) and resets the memoised seed so the next <see cref="EdictChaos.CurrentSeed"/>
/// read re-resolves under the scoped value. Restores the previous environment value
/// and resets again on dispose, so the resolver returns to its run-wide behaviour.
/// </summary>
sealed class ChaosSeedScope : IDisposable
{
    const string EnvironmentVariable = "EDICT_CHAOS_SEED";
    readonly string? _previousValue;

    public ChaosSeedScope(string? rawValue)
    {
        _previousValue = Environment.GetEnvironmentVariable(EnvironmentVariable);
        Environment.SetEnvironmentVariable(EnvironmentVariable, rawValue);
        EdictChaos.ResetMemoizedSeedForTesting();
    }

    public ChaosSeedScope(int seed)
        : this(seed.ToString(CultureInfo.InvariantCulture))
    {
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvironmentVariable, _previousValue);
        EdictChaos.ResetMemoizedSeedForTesting();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ChaosSeedCollection
{
    public const string Name = "ChaosSeed";
}
