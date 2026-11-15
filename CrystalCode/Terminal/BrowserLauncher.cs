using System.Diagnostics;

namespace CrystalCode.Terminal;

/// <summary>
/// Opens a link in the operator's default browser. A failure only means the
/// caller should show the link instead.
/// </summary>
public static class BrowserLauncher
{
    public static Task<bool> OpenAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        cancellationToken.ThrowIfCancellationRequested();
        if (link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp)
        {
            return Task.FromResult(false);
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true });
            return Task.FromResult(process is not null || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows());
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or PlatformNotSupportedException)
        {
            return Task.FromResult(false);
        }
    }
}
