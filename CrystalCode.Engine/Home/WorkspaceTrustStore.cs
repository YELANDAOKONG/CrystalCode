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

    public bool Contains(string workspaceRoot)
    {
        var trustRoot = GitRoot.TrustRoot(workspaceRoot);
        return Read().Directories.Any(path => Same(path, trustRoot));
    }

    public void Remember(string workspaceRoot)
    {
        var trustRoot = GitRoot.TrustRoot(workspaceRoot);
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
        var trustRoot = GitRoot.TrustRoot(workspaceRoot);
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
