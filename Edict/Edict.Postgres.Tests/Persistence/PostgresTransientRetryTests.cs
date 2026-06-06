using Edict.Postgres.Persistence;

using Npgsql;

using Xunit;

namespace Edict.Postgres.Tests.Persistence;

/// <summary>
/// Isolated exercises of the delegate-based transient-retry helper. No database:
/// the transient trigger is fabricated as <c>new NpgsqlException(message, new IOException())</c>,
/// which reports <c>IsTransient == true</c>, and the non-transient trigger is a bare
/// <see cref="NpgsqlException"/>, which reports <c>IsTransient == false</c>.
/// </summary>
public sealed class PostgresTransientRetryTests
{
    static readonly PostgresTransientRetry.Policy FastPolicy = new(TotalAttempts: 3, BaseDelay: TimeSpan.FromMilliseconds(1));

    [Fact]
    public async Task ExecuteAsync_RecoversAfterTransientFault_ReturnsResultAndSignalsRecovered()
    {
        // Arrange
        var attempts = 0;
        var phases = new List<PostgresTransientRetry.Phase>();

        // Act
        var result = await PostgresTransientRetry.ExecuteAsync(
            operation: async () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new NpgsqlException("transient socket reset", new IOException());
                }
                await Task.Yield();
                return "ok";
            },
            policy: FastPolicy,
            isTransient: EdictPostgresGrainStorage.IsTransientFault,
            hook: (phase, _, _) => phases.Add(phase));

        // Assert
        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
        Assert.Equal([PostgresTransientRetry.Phase.Retrying, PostgresTransientRetry.Phase.Recovered], phases);
    }

    [Fact]
    public async Task ExecuteAsync_ExhaustsTransientBudget_RethrowsLastFaultAndSignalsExhausted()
    {
        // Arrange
        var attempts = 0;
        var phases = new List<PostgresTransientRetry.Phase>();

        // Act
        var thrown = await Assert.ThrowsAsync<NpgsqlException>(() =>
            PostgresTransientRetry.ExecuteAsync<string>(
                operation: () =>
                {
                    attempts++;
                    throw new NpgsqlException($"transient #{attempts}", new IOException());
                },
                policy: FastPolicy,
                isTransient: EdictPostgresGrainStorage.IsTransientFault,
                hook: (phase, _, _) => phases.Add(phase)));

        // Assert
        Assert.Equal(3, attempts);
        Assert.Equal("transient #3", thrown.Message);
        Assert.Equal(
            [PostgresTransientRetry.Phase.Retrying, PostgresTransientRetry.Phase.Retrying, PostgresTransientRetry.Phase.Exhausted],
            phases);
    }

    [Fact]
    public async Task ExecuteAsync_NonTransientFault_ThrowsImmediatelyWithoutRetryOrSignal()
    {
        // Arrange
        var attempts = 0;
        var phases = new List<PostgresTransientRetry.Phase>();

        // Act
        var thrown = await Assert.ThrowsAsync<NpgsqlException>(() =>
            PostgresTransientRetry.ExecuteAsync<string>(
                operation: () =>
                {
                    attempts++;
                    throw new NpgsqlException("non-transient");
                },
                policy: FastPolicy,
                isTransient: EdictPostgresGrainStorage.IsTransientFault,
                hook: (phase, _, _) => phases.Add(phase)));

        // Assert — single invocation proves no retry and therefore no delay.
        Assert.Equal(1, attempts);
        Assert.Equal("non-transient", thrown.Message);
        Assert.Empty(phases);
    }
}
