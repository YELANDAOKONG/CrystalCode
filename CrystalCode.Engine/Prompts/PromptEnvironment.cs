using System.Globalization;
using System.Runtime.InteropServices;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Host-owned environment snapshot for Work and Plan placeholders. Not overlayable.
/// </summary>
public static class PromptEnvironment
{
    public static string Render(
        string workspaceRoot,
        string provider,
        string model,
        DateTimeOffset? now = null,
        string sessionId = "",
        string approval = "")
    {
        var snapshot = CreateSnapshot(workspaceRoot, provider, model, now, sessionId, approval);
        return FormatBlock(snapshot);
    }

    public static PromptEnvironmentSnapshot CreateSnapshot(
        string workspaceRoot,
        string provider,
        string model,
        DateTimeOffset? now = null,
        string sessionId = "",
        string approval = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(approval);

        var stamp = now ?? DateTimeOffset.Now;
        var gitPath = Path.Combine(workspaceRoot, ".git");
        var git = Directory.Exists(gitPath) || File.Exists(gitPath) ? "yes" : "no";
        return new PromptEnvironmentSnapshot(
            Path.GetFullPath(workspaceRoot),
            git,
            PlatformName(),
            OperatingSystemDescription(),
            ArchitectureName(),
            stamp.ToString("dddd MMM d, yyyy", CultureInfo.InvariantCulture),
            stamp.ToString("HH:mm:ss zzz", CultureInfo.InvariantCulture),
            provider.Trim(),
            model.Trim(),
            sessionId.Trim(),
            approval.Trim());
    }

    public static string FormatBlock(PromptEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var lines = new List<string>
        {
            "<env>",
            $"  Workspace: {snapshot.Workspace}",
            $"  Is git repo: {snapshot.IsGitRepo}",
            $"  Platform: {snapshot.Platform}",
            $"  OS: {snapshot.Os}",
            $"  Architecture: {snapshot.Architecture}",
            $"  Today's date: {snapshot.Date}",
            $"  Local time: {snapshot.Time}",
            $"  Model: {snapshot.Provider} / {snapshot.Model}"
        };
        if (snapshot.SessionId.Length > 0)
        {
            lines.Add($"  Session: {snapshot.SessionId}");
        }

        if (snapshot.Approval.Length > 0)
        {
            lines.Add($"  Approval: {snapshot.Approval}");
        }

        lines.Add("</env>");
        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatBlock(PromptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return FormatBlock(
            new PromptEnvironmentSnapshot(
                context.Workspace,
                context.IsGitRepo,
                context.Platform,
                context.Os,
                context.Architecture,
                context.Date,
                context.Time,
                context.Provider,
                context.Model,
                context.SessionId,
                context.Approval));
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

    private static string OperatingSystemDescription()
    {
        var description = RuntimeInformation.OSDescription.Trim();
        return description.Length == 0 ? PlatformName() : description;
    }

    private static string ArchitectureName() =>
        RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
}
