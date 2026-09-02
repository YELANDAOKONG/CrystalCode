using System.Globalization;

namespace CrystalCode.Prompts;

/// <summary>
/// Host-owned environment snapshot for Work and Plan placeholders. Not overlayable.
/// </summary>
public static class PromptEnvironment
{
    public static string Render(
        string workspaceRoot,
        string provider,
        string model,
        DateTimeOffset? now = null)
    {
        var snapshot = CreateSnapshot(workspaceRoot, provider, model, now);
        return FormatBlock(snapshot);
    }

    public static PromptEnvironmentSnapshot CreateSnapshot(
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
        return new PromptEnvironmentSnapshot(
            Path.GetFullPath(workspaceRoot),
            git,
            PlatformName(),
            stamp.ToString("dddd MMM d, yyyy", CultureInfo.InvariantCulture),
            provider.Trim(),
            model.Trim());
    }

    public static string FormatBlock(PromptEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return
            $"""
            <env>
              Workspace: {snapshot.Workspace}
              Is git repo: {snapshot.IsGitRepo}
              Platform: {snapshot.Platform}
              Today's date: {snapshot.Date}
              Model: {snapshot.Provider} / {snapshot.Model}
            </env>
            """;
    }

    public static string FormatBlock(PromptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return FormatBlock(
            new PromptEnvironmentSnapshot(
                context.Workspace,
                context.IsGitRepo,
                context.Platform,
                context.Date,
                context.Provider,
                context.Model));
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
