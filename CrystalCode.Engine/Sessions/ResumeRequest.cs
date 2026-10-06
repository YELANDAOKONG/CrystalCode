using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// How <c>/resume</c> and <c>--resume</c> select a saved session.
/// <c>all</c> is reserved. A path, <c>.</c>, <c>..</c>, or an existing directory
/// names a workspace. Anything else is a session id.
/// </summary>
public sealed record ResumeRequest
{
    /// <summary>Which sessions the resume selector offers.</summary>
    public enum Kind
    {
        /// <summary>Sessions for the current workspace.</summary>
        CurrentWorkspace,

        /// <summary>One session id, with no selector.</summary>
        Session,

        /// <summary>Sessions from every workspace.</summary>
        AllWorkspaces,

        /// <summary>Sessions for one named directory.</summary>
        Workspace
    }

    private ResumeRequest(Kind target, string? value)
    {
        Target = target;
        Value = value;
    }

    /// <summary>Selector scope, or a direct session id.</summary>
    public Kind Target { get; }

    /// <summary>Session id, or the canonical workspace directory.</summary>
    public string? Value { get; }

    /// <summary>
    /// Parses a resume argument. <paramref name="baseDirectory"/> resolves
    /// relative workspace paths and must already exist.
    /// </summary>
    public static bool TryParse(
        string? argument,
        string baseDirectory,
        SessionStore store,
        out ResumeRequest request,
        out string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(store);
        request = new ResumeRequest(Kind.CurrentWorkspace, null);
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(argument))
        {
            return true;
        }

        var text = argument.Trim();
        if (text.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            request = new ResumeRequest(Kind.AllWorkspaces, null);
            return true;
        }

        var workspace = new Workspace(Path.GetFullPath(baseDirectory));
        var pathShaped = IsPathShaped(text);
        var directoryExists = workspace.TryResolve(text, out var candidate, out var resolveError);
        var sessionExists = store.TryLoad(text, out _);
        if (pathShaped)
        {
            if (!directoryExists)
            {
                error = resolveError;
                return false;
            }

            if (sessionExists)
            {
                error = AmbiguousTarget;
                return false;
            }

            request = new ResumeRequest(Kind.Workspace, candidate);
            return true;
        }

        if (directoryExists && sessionExists)
        {
            error = AmbiguousTarget;
            return false;
        }

        if (directoryExists)
        {
            request = new ResumeRequest(Kind.Workspace, candidate);
            return true;
        }

        request = new ResumeRequest(Kind.Session, text);
        return true;
    }

    /// <summary>
    /// True when both paths name the same existing directory, or the same
    /// unresolved path when a directory is missing.
    /// </summary>
    public static bool SameDirectory(string left, string right)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);
        return string.Equals(
            NormalizeDirectory(left),
            NormalizeDirectory(right),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves a saved session workspace to a canonical directory.
    /// </summary>
    public static bool TryCanonicalWorkspace(
        string? path,
        out string canonical,
        out string error)
    {
        canonical = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = MissingWorkspace;
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(Workspace.Expand(path.Trim()));
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or IOException)
        {
            error = MissingWorkspace;
            return false;
        }

        if (!Directory.Exists(full))
        {
            error = MissingWorkspace;
            return false;
        }

        canonical = new Workspace(full).Root;
        return true;
    }

    private const string AmbiguousTarget =
        "Resume target matches both a session and a directory.";

    private const string MissingWorkspace = "Session workspace is not a directory.";

    private static bool IsPathShaped(string text)
    {
        if (text is "." or "..")
        {
            return true;
        }

        if (text.Contains('/') || text.Contains('\\'))
        {
            return true;
        }

        if (text == "~")
        {
            return true;
        }

        return text.StartsWith("~/", StringComparison.Ordinal)
            || text.StartsWith("~\\", StringComparison.Ordinal);
    }

    private static string NormalizeDirectory(string path)
    {
        var full = Path.GetFullPath(Workspace.Expand(path.Trim()));
        if (!Directory.Exists(full))
        {
            return full;
        }

        return new Workspace(full).Root;
    }
}
