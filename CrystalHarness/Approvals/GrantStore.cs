using System.Text.Json;

using Crystal.Tools;

using CrystalHarness.Home;

namespace CrystalHarness.Approvals;

/// <summary>
/// Remembers session and persistent approval grants.
/// </summary>
public sealed class GrantStore
{
    private readonly CrystalHome _home;
    private readonly HashSet<string> _session = new(StringComparer.Ordinal);

    public GrantStore(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    public bool Contains(string workspaceRoot, ToolCall call)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(call);
        var key = Key(workspaceRoot, call);
        if (_session.Contains(key))
        {
            return true;
        }

        return PersistentKeys().Contains(key);
    }

    public void Remember(string workspaceRoot, ToolCall call, GrantScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(scope);
        if (scope == GrantScope.Once)
        {
            return;
        }

        var key = Key(workspaceRoot, call);
        _session.Add(key);
        if (scope != GrantScope.Persistent)
        {
            return;
        }

        SavePersistent(workspaceRoot, call);
    }

    private void SavePersistent(string workspaceRoot, ToolCall call)
    {
        _home.EnsureCreated();
        var document = ReadDocument();
        var workspace = Path.GetFullPath(workspaceRoot);
        var fingerprint = GrantFingerprint.Create(call);
        if (document.Grants.Any(grant =>
                string.Equals(grant.Workspace, workspace, StringComparison.Ordinal)
                && string.Equals(grant.Tool, call.Name, StringComparison.Ordinal)
                && string.Equals(grant.Fingerprint, fingerprint, StringComparison.Ordinal)))
        {
            return;
        }

        document.Grants.Add(
            new PermissionGrantDocument
            {
                Workspace = workspace,
                Tool = call.Name,
                Fingerprint = fingerprint
            });
        File.WriteAllText(
            _home.PermissionsPath,
            JsonSerializer.Serialize(document, HomeJson.Options));
        RestrictOwnerAccess(_home.PermissionsPath);
    }

    private HashSet<string> PersistentKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in ReadDocument().Grants)
        {
            if (string.IsNullOrWhiteSpace(grant.Workspace)
                || string.IsNullOrWhiteSpace(grant.Tool)
                || string.IsNullOrWhiteSpace(grant.Fingerprint))
            {
                continue;
            }

            keys.Add(Compose(grant.Workspace, grant.Tool, grant.Fingerprint));
        }

        return keys;
    }

    private PermissionDocument ReadDocument()
    {
        if (!File.Exists(_home.PermissionsPath))
        {
            return new PermissionDocument();
        }

        var json = File.ReadAllText(_home.PermissionsPath);
        return JsonSerializer.Deserialize<PermissionDocument>(json, HomeJson.Options)
            ?? new PermissionDocument();
    }

    private static string Key(string workspaceRoot, ToolCall call) =>
        Compose(Path.GetFullPath(workspaceRoot), call.Name, GrantFingerprint.Create(call));

    private static string Compose(string workspace, string tool, string fingerprint) =>
        workspace + "\n" + tool + "\n" + fingerprint;

    private static void RestrictOwnerAccess(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
