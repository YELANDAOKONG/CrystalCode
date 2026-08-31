using System.Globalization;
using System.Net.Http.Headers;

namespace CrystalCode.Providers.Compatible;

internal static class CompatibleRetryAfter
{
    public static TimeSpan? Read(HttpResponseHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (headers.TryGetValues("retry-after-ms", out var milliseconds))
        {
            foreach (var value in milliseconds)
            {
                if (double.TryParse(
                        value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var parsed)
                    && parsed >= 0)
                {
                    return Cap(TimeSpan.FromMilliseconds(parsed));
                }
            }
        }

        var retryAfter = headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta >= TimeSpan.Zero)
        {
            return Cap(delta);
        }

        if (retryAfter.Date is { } date)
        {
            var remaining = date.UtcDateTime - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                return Cap(remaining);
            }
        }

        return null;
    }

    private static TimeSpan Cap(TimeSpan value)
    {
        var maximum = TimeSpan.FromMilliseconds(int.MaxValue);
        return value > maximum ? maximum : value;
    }
}
