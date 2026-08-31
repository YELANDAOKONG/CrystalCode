using System.Globalization;

namespace CrystalHarness.Prompts;

/// <summary>
/// Host-owned environment block appended to Work and Plan. Not overlayable.
/// </summary>
public static class PromptEnvironment
{
    public static string Render(
        string workspaceRoot,
        string provider,
        string model,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var stamp = now ?? DateTimeOffset.Now;
        var gitPath = Path.Combine(workspaceRoot, ".git");
        var git = Directory.Exists(gitPath) || File.Exists(gitPath) ? "yes" : "no";
        return
            $"""
            <env>
              Workspace: {Path.GetFullPath(workspaceRoot)}
              Is git repo: {git}
              Platform: {PlatformName()}
              Today's date: {stamp.ToString("dddd MMM d, yyyy", CultureInfo.InvariantCulture)}
              Model: {provider.Trim()} / {model.Trim()}
            </env>
            """;
    }

    private static string PlatformName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "osx";
        }

        return "linux";
    }
}
