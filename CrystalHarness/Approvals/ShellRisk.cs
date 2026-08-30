using System.Text.RegularExpressions;

namespace CrystalHarness.Approvals;

internal static class ShellRisk
{
    private static readonly Regex ForbiddenPattern = new(
        """
        \bsudo\b
        |rm\s+-[a-zA-Z]*r[a-zA-Z]*f[a-zA-Z]*\s+(?:/|~(?:/|$)|\$HOME(?:/|$)|\$\{HOME\}(?:/|$))
        |(?:curl|wget)\b[\s\S]*\|\s*(?:ba)?sh\b
        |\bgit\s+push\b[\s\S]*\s(?:-f|--force)\b
        |(?:^|[^\w.])(?:~|/)?\.ssh(?:/|\b)
        |(?:^|[^\w.])(?:~|/)?\.gnupg(?:/|\b)
        """,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace,
        TimeSpan.FromSeconds(1));

    private static readonly Regex PrivilegedPattern = new(
        """
        \bchmod\s+777\b
        |\bchown\b
        |\bmkfs\b
        |\bdd\s+
        |\bkill\s+-9\b
        |\b(?:shutdown|reboot)\b
        """,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace,
        TimeSpan.FromSeconds(1));

    private static readonly Regex NetworkPattern = new(
        """\b(?:curl|wget|ssh|scp|nc|nmap)\b""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static (Risk Risk, Authority Authority, string Summary) Classify(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (IsMatch(ForbiddenPattern, command))
        {
            return (Risk.Forbidden, Authority.PrivilegedEscalation, "Forbidden shell command");
        }

        if (IsMatch(PrivilegedPattern, command))
        {
            return (Risk.Privileged, Authority.PrivilegedEscalation, "Privileged shell command");
        }

        if (IsMatch(NetworkPattern, command))
        {
            return (Risk.Write, Authority.Network, "Network shell command");
        }

        return (Risk.Write, Authority.Workspace, "Workspace shell command");
    }

    private static bool IsMatch(Regex regex, string command)
    {
        try
        {
            return regex.IsMatch(command);
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }
}
