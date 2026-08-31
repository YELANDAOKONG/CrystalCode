namespace CrystalCode.Display.Shell;

/// <summary>
/// Compact elapsed labels for the progress row: 5s, 2m18s, 1h2m3s.
/// </summary>
public static class ProgressElapsed
{
    private const int SecondsPerMinute = 60;

    private const int SecondsPerHour = 3600;

    public static string Format(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var totalSeconds = (int)elapsed.TotalSeconds;
        var hours = totalSeconds / SecondsPerHour;
        var minutes = totalSeconds % SecondsPerHour / SecondsPerMinute;
        var seconds = totalSeconds % SecondsPerMinute;
        if (hours > 0)
        {
            return $"{hours}h{minutes}m{seconds}s";
        }

        if (minutes > 0)
        {
            return $"{minutes}m{seconds}s";
        }

        return $"{seconds}s";
    }
}
