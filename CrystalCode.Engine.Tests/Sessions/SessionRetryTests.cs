using CrystalCode.Engine.Sessions;
using CrystalCode.Providers.DeepSeek;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionRetryTests
{
    [Fact]
    public void Delay_CapsAtThirtySecondsWithoutHeaders()
    {
        var delays = Enumerable.Range(1, 10)
            .Select(attempt => SessionRetry.Delay(SessionRetryOptions.Default, attempt, retryAfter: null, jitter: 0))
            .ToArray();

        Assert.Equal(
            [
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(16),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30)
            ],
            delays);
    }

    [Fact]
    public void Delay_AddsJitter()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), SessionRetry.Delay(SessionRetryOptions.Default, 1, null, 0));
        Assert.Equal(TimeSpan.FromMilliseconds(2500), SessionRetry.Delay(SessionRetryOptions.Default, 1, null, 1));
        Assert.Equal(TimeSpan.FromSeconds(20), SessionRetry.Delay(SessionRetryOptions.Default, 4, null, 1));
        Assert.Equal(TimeSpan.FromSeconds(30), SessionRetry.Delay(SessionRetryOptions.Default, 5, null, 1));
    }

    [Fact]
    public void Delay_HonorsRetryAfterBeyondThirtySeconds()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(50),
            SessionRetry.Delay(SessionRetryOptions.Default, 1, TimeSpan.FromSeconds(50), jitter: 0));
    }

    [Fact]
    public void Delay_UsesConfiguredPolicy()
    {
        var options = new SessionRetryOptions(
            initialDelay: TimeSpan.FromSeconds(1),
            backoffFactor: 3,
            maximumDelayWithoutHeaders: TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(1), SessionRetry.Delay(options, 1, null, 0));
        Assert.Equal(TimeSpan.FromSeconds(3), SessionRetry.Delay(options, 2, null, 0));
        Assert.Equal(TimeSpan.FromSeconds(5), SessionRetry.Delay(options, 3, null, 0));
    }

    [Fact]
    public void Delay_ClampsHugeBackoffBeforeOverflow()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            SessionRetry.Delay(SessionRetryOptions.Default, 40, null, 0));

        var configured = new SessionRetryOptions(
            initialDelay: TimeSpan.FromSeconds(3_600),
            backoffFactor: 100,
            maximumDelayWithoutHeaders: TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), SessionRetry.Delay(configured, 6, null, 0));
    }

    [Fact]
    public void Constructor_RejectsNonFiniteJitterAndBackoff()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionRetryOptions(backoffFactor: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionRetryOptions(jitterFactor: double.PositiveInfinity));
    }

    [Fact]
    public void TryDescribe_Retries429()
    {
        var exception = new DeepSeekException("slow down", statusCode: 429);
        Assert.True(SessionRetry.TryDescribe(exception, out var retryable));
        Assert.Equal("slow down", retryable.Message);
    }

    [Fact]
    public void TryDescribe_Retries5xx()
    {
        var exception = new DeepSeekException("Internal server error", statusCode: 502);
        Assert.True(SessionRetry.TryDescribe(exception, out _));
    }

    [Fact]
    public void TryDescribe_DoesNotRetry401()
    {
        var exception = new DeepSeekException("Unauthorized", statusCode: 401);
        Assert.False(SessionRetry.TryDescribe(exception, out _));
    }

    [Fact]
    public void TryDescribe_DoesNotRetryQuota()
    {
        var exception = new DeepSeekException(
            "Quota exceeded",
            statusCode: 429,
            errorCode: "insufficient_quota");
        Assert.False(SessionRetry.TryDescribe(exception, out _));
    }

    [Fact]
    public void TryDescribe_DoesNotRetryContextOverflow()
    {
        var exception = new DeepSeekException(
            "Input exceeds context window of this model",
            statusCode: 400,
            errorCode: "context_length_exceeded");
        Assert.False(SessionRetry.TryDescribe(exception, out _));
    }

    [Fact]
    public void TryDescribe_DoesNotRetry400WithoutTransientText()
    {
        var exception = new DeepSeekException(
            "Chat request failed (HTTP 400, code: invalid_request). bad model",
            statusCode: 400,
            errorCode: "invalid_request");
        Assert.False(SessionRetry.TryDescribe(exception, out _));
    }

    [Fact]
    public void TryDescribe_RetriesIncompleteStream()
    {
        var exception = new DeepSeekException(
            "DeepSeek chat stream ended before every candidate completed.");
        Assert.True(SessionRetry.TryDescribe(exception, out _));
    }

    [Fact]
    public void TryDescribe_DoesNotRetryInvalidRequestStreamError()
    {
        var exception = new DeepSeekException(
            "DeepSeek chat stream failed (code: invalid_request_error). too many images",
            errorCode: "invalid_request_error");
        Assert.False(SessionRetry.TryDescribe(exception, out _));
    }

    [Fact]
    public void TryDescribe_RetriesTransientStreamError()
    {
        var exception = new DeepSeekException(
            "DeepSeek chat stream failed (code: rate_limit_exceeded). rate limit exceeded",
            errorCode: "rate_limit_exceeded");
        Assert.True(SessionRetry.TryDescribe(exception, out _));
    }

    [Fact]
    public void TryDescribe_RetriesWrappedNetworkFailure()
    {
        var exception = new DeepSeekException(
            "DeepSeek chat request failed.",
            innerException: new HttpRequestException("Connection refused"));
        Assert.True(SessionRetry.TryDescribe(exception, out var retryable));
        Assert.Equal("DeepSeek chat request failed.", retryable.Message);
    }

    [Fact]
    public void TryDescribe_UsesRetryAfterFromFault()
    {
        var exception = new DeepSeekException(
            "slow down",
            statusCode: 429,
            retryAfter: TimeSpan.FromSeconds(8));
        Assert.True(SessionRetry.TryDescribe(exception, out var retryable));
        Assert.Equal(TimeSpan.FromSeconds(8), retryable.RetryAfter);
    }

    [Fact]
    public async Task RunAsync_RetriesThenSucceeds()
    {
        var calls = 0;
        var waits = new List<TimeSpan>();
        var options = new SessionRetryOptions(
            5,
            (delay, _) =>
            {
                waits.Add(delay);
                return Task.CompletedTask;
            },
            () => 0);

        var result = await SessionRetry.RunAsync(
            _ =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new DeepSeekException("slow down", statusCode: 429);
                }

                return Task.FromResult(7);
            },
            options,
            onRetry: null,
            CancellationToken.None);

        Assert.Equal(7, result);
        Assert.Equal(2, calls);
        Assert.Equal([TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task RunAsync_StopsAfterMaximumRetries()
    {
        var calls = 0;
        var options = new SessionRetryOptions(
            2,
            (_, _) => Task.CompletedTask,
            () => 0);

        var exception = await Assert.ThrowsAsync<DeepSeekException>(
            () => SessionRetry.RunAsync<int>(
                _ =>
                {
                    calls++;
                    throw new DeepSeekException("slow down", statusCode: 429);
                },
                options,
                onRetry: null,
                CancellationToken.None));

        Assert.Equal(3, calls);
        Assert.Equal(429, exception.StatusCode);
    }

    [Fact]
    public async Task RunAsync_DoesNotRetryUserCancel()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var calls = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => SessionRetry.RunAsync<int>(
                token =>
                {
                    calls++;
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult(0);
                },
                SessionRetryOptions.Default,
                onRetry: null,
                cts.Token));

        Assert.Equal(1, calls);
    }
}
