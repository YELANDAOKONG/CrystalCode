using System.Globalization;
using System.Runtime.InteropServices;

using CrystalCode.Engine.Tools;

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
        var fullWorkspace = Path.GetFullPath(workspaceRoot);
        var gitPath = Path.Combine(fullWorkspace, ".git");
        var git = Directory.Exists(gitPath) || File.Exists(gitPath) ? "yes" : "no";
        return new PromptEnvironmentSnapshot(
            fullWorkspace,
            git,
            GitRoot.Find(fullWorkspace) ?? string.Empty,
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
            $"  Is git repo: {snapshot.IsGitRepo}"
        };
        if (snapshot.GitRoot.Length > 0)
        {
            lines.Add($"  Git root: {snapshot.GitRoot}");
        }

        lines.Add($"  Platform: {snapshot.Platform}");
        lines.Add($"  OS: {snapshot.Os}");
        lines.Add($"  Architecture: {snapshot.Architecture}");
        lines.Add("</env>");
        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatClock(string date, string time)
    {
        ArgumentNullException.ThrowIfNull(date);
        ArgumentNullException.ThrowIfNull(time);
        if (date.Length == 0 && time.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            "<clock>",
            $"  Today's date: {date}",
            $"  Local time: {time}",
            "</clock>");
    }

    public static string FormatModel(string provider, string model)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(model);
        if (provider.Length == 0 && model.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            "<model>",
            $"  Provider: {provider}",
            $"  Model: {model}",
            "</model>");
    }

    public static string FormatSession(string sessionId, string approval)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(approval);
        var lines = new List<string>();
        if (sessionId.Length > 0)
        {
            lines.Add($"  Session: {sessionId}");
        }

        if (approval.Length > 0)
        {
            lines.Add($"  Approval: {approval}");
        }

        return lines.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, ["<session>", .. lines, "</session>"]);
    }

    public static string FormatBlock(PromptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return FormatBlock(
            new PromptEnvironmentSnapshot(
                context.Workspace,
                context.IsGitRepo,
                context.GitRoot,
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
