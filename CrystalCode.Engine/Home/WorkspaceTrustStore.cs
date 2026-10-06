using System.Text.Json;

using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Home;

/// <summary>
/// Remembers directories the operator trusted. The file lives under Home,
/// never in the workspace, so a project cannot grant trust to itself.
/// </summary>
public sealed class WorkspaceTrustStore
{
    private readonly CrystalHome _home;

    public WorkspaceTrustStore(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    /// <summary>
    /// Directory a trust decision applies to. The operator space is its own
    /// root even when a parent directory is a git repository. Otherwise this
    /// is <see cref="GitRoot.TrustRoot"/>.
    /// </summary>
    public string TrustRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var canonical = Workspace.Canonicalize(Path.GetFullPath(workspaceRoot));
        if (OperatorSpace.Is(_home, canonical))
        {
            return OperatorSpace.Resolve(_home);
        }

        return GitRoot.TrustRoot(canonical);
    }

    public bool Contains(string workspaceRoot)
    {
        var trustRoot = TrustRoot(workspaceRoot);
        if (OperatorSpace.Is(_home, trustRoot))
        {
            return true;
        }

        return Read().Directories.Any(path => Same(path, trustRoot));
    }

    public void Remember(string workspaceRoot)
    {
        var trustRoot = TrustRoot(workspaceRoot);
        if (OperatorSpace.Is(_home, trustRoot))
        {
            return;
        }

        var document = Read();
        if (document.Directories.Any(path => Same(path, trustRoot)))
        {
            return;
        }

        document.Directories.Add(trustRoot);
        Write(document);
    }

    public void Forget(string workspaceRoot)
    {
        var trustRoot = TrustRoot(workspaceRoot);
        if (OperatorSpace.Is(_home, trustRoot))
        {
            return;
        }

        var document = Read();
        var removed = document.Directories.RemoveAll(path => Same(path, trustRoot));
        if (removed == 0)
        {
            return;
        }

        Write(document);
    }

    private TrustedDirectoryDocument Read()
    {
        if (!File.Exists(_home.TrustedPath))
        {
            return new TrustedDirectoryDocument();
        }

        try
        {
            var document = JsonSerializer.Deserialize<TrustedDirectoryDocument>(
                    File.ReadAllText(_home.TrustedPath),
                    HomeJson.Options)
                ?? new TrustedDirectoryDocument();
            document.Directories ??= [];
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "trusted.json could not be read.",
                exception);
        }
    }

    private void Write(TrustedDirectoryDocument document)
    {
        _home.EnsureCreated();
        File.WriteAllText(
            _home.TrustedPath,
            JsonSerializer.Serialize(document, HomeJson.Options));
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            _home.TrustedPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static bool Same(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.Ordinal);
}
