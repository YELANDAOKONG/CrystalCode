using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Configuration;

public sealed class RetrySettingsTests
{
    [Fact]
    public void Default_MatchesSessionRetryDefaults()
    {
        var options = RetrySettings.Default.ToOptions();

        Assert.Equal(SessionRetryOptions.DefaultMaximumRetries, options.MaximumRetries);
        Assert.Equal(
            TimeSpan.FromSeconds(SessionRetryOptions.DefaultInitialDelaySeconds),
            options.InitialDelay);
        Assert.Equal(SessionRetryOptions.DefaultBackoffFactor, options.BackoffFactor);
        Assert.Equal(SessionRetryOptions.DefaultJitterFactor, options.JitterFactor);
        Assert.Equal(
            TimeSpan.FromSeconds(SessionRetryOptions.DefaultMaximumDelaySeconds),
            options.MaximumDelayWithoutHeaders);
    }

    [Fact]
    public void ToOptions_DisablesRetriesWhenDisabled()
    {
        var options = new RetrySettings(enabled: false, maximumRetries: 3).ToOptions();

        Assert.Equal(0, options.MaximumRetries);
    }

    [Theory]
    [InlineData(-1, 1, 0.25, 1)]
    [InlineData(1, 101, 0.25, 1)]
    [InlineData(1, 2, 1.5, 1)]
    [InlineData(1, 2, 0.25, -1)]
    public void Constructor_RejectsOutOfRangeValues(
        double initialDelaySeconds,
        double backoffFactor,
        double jitterFactor,
        double maximumDelaySeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RetrySettings(
                initialDelaySeconds: initialDelaySeconds,
                backoffFactor: backoffFactor,
                jitterFactor: jitterFactor,
                maximumDelaySeconds: maximumDelaySeconds));
    }
}
