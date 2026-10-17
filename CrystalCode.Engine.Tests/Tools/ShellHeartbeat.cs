namespace CrystalCode.Engine.Tests.Tools;

/// <summary>
/// Watches a shell that appends to a file until it is stopped.
/// Git Bash <c>$$</c> is an MSYS pid, not a Win32 pid, so
/// <see cref="System.Diagnostics.Process.GetProcessById(int)"/> can observe an
/// unrelated live process after the shell has already exited.
/// </summary>
internal static class ShellHeartbeat
{
    public const string Command = "while true; do printf x >> beat.txt; done";

    public static async Task WaitUntilWritingAsync(string path)
    {
        var started = DateTimeOffset.UtcNow;
        long previous = -1;
        while (DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5))
        {
            if (File.Exists(path))
            {
                var length = new FileInfo(path).Length;
                if (previous >= 0 && length > previous)
                {
                    return;
                }

                previous = length;
            }

            await Task.Delay(20);
        }

        throw new Xunit.Sdk.XunitException("The shell did not start writing.");
    }

    public static async Task AssertStoppedAsync(string path)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        long previous = -1;
        var stableSince = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var length = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (length == previous)
            {
                if (DateTimeOffset.UtcNow - stableSince >= TimeSpan.FromMilliseconds(300))
                {
                    return;
                }
            }
            else
            {
                previous = length;
                stableSince = DateTimeOffset.UtcNow;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException("The shell kept writing after it was stopped.");
    }
}
