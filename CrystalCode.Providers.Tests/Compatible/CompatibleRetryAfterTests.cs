using System.Net;
using System.Net.Http.Headers;

using CrystalCode.Providers.Compatible;

using Xunit;

namespace CrystalCode.Providers.Tests.Compatible;

public sealed class CompatibleRetryAfterTests
{
    [Fact]
    public void Read_PrefersRetryAfterMilliseconds()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        Assert.True(response.Headers.TryAddWithoutValidation("retry-after-ms", "1500"));

        Assert.Equal(TimeSpan.FromMilliseconds(1500), CompatibleRetryAfter.Read(response.Headers));
    }

    [Fact]
    public void Read_UsesRetryAfterDelta()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(8));

        Assert.Equal(TimeSpan.FromSeconds(8), CompatibleRetryAfter.Read(response.Headers));
    }

    [Fact]
    public void Read_IgnoresPastHttpDate()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(
            new DateTimeOffset(DateTime.UtcNow.AddSeconds(-5), TimeSpan.Zero));

        Assert.Null(CompatibleRetryAfter.Read(response.Headers));
    }
}
