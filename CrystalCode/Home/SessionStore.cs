using System.Text.Json;

namespace CrystalCode.Home;

/// <summary>
/// Reads and writes session files under <c>~/.crystal/sessions</c>.
/// </summary>
public sealed class SessionStore
{
    private readonly CrystalHome _home;

    public SessionStore(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    public static string NewId() => Guid.NewGuid().ToString("N");

    public void Save(SessionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            throw new ArgumentException("Session id is required.", nameof(document));
        }

        _home.EnsureCreated();
        document.UpdatedUtc = DateTimeOffset.UtcNow;
        document.CreatedUtc ??= document.UpdatedUtc;
        var path = PathFor(document.Id);
        var json = JsonSerializer.Serialize(document, HomeJson.Options);
        File.WriteAllText(path, json);
    }

    public bool TryLoad(string id, out SessionDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        document = null!;
        var path = PathFor(id.Trim());
        if (!File.Exists(path))
        {
            return false;
        }

        return TryRead(path, out document);
    }

    public bool TryLoadLatest(string workspaceRoot, out SessionDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        document = null!;
        var workspace = Path.GetFullPath(workspaceRoot);
        SessionDocument? latest = null;
        foreach (var path in EnumerateFiles())
        {
            if (!TryRead(path, out var candidate)
                || string.IsNullOrWhiteSpace(candidate.Workspace))
            {
                continue;
            }

            if (!string.Equals(
                    Path.GetFullPath(candidate.Workspace),
                    workspace,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (latest is null
                || (candidate.UpdatedUtc ?? DateTimeOffset.MinValue)
                    > (latest.UpdatedUtc ?? DateTimeOffset.MinValue))
            {
                latest = candidate;
            }
        }

        if (latest is null)
        {
            return false;
        }

        document = latest;
        return true;
    }

    private IEnumerable<string> EnumerateFiles()
    {
        if (!Directory.Exists(_home.SessionsDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_home.SessionsDirectory, "*.json");
    }

    private bool TryRead(string path, out SessionDocument document)
    {
        document = null!;
        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<SessionDocument>(json, HomeJson.Options);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Id))
            {
                return false;
            }

            document = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string PathFor(string id) =>
        Path.Combine(_home.SessionsDirectory, id + ".json");
}
