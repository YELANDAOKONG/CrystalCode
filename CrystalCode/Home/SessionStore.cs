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
        var id = document.Id?.Trim();
        if (string.IsNullOrWhiteSpace(id) || !IsValidId(id))
        {
            throw new ArgumentException("Session id is invalid.", nameof(document));
        }

        _home.EnsureCreated();
        document.Id = id;
        document.UpdatedUtc = DateTimeOffset.UtcNow;
        document.CreatedUtc ??= document.UpdatedUtc;
        var path = PathFor(id);
        var json = JsonSerializer.Serialize(document, HomeJson.Options);
        File.WriteAllText(path, json);
    }

    public bool TryLoad(string id, out SessionDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        document = null!;
        var normalized = id.Trim();
        if (!IsValidId(normalized))
        {
            return false;
        }

        var path = PathFor(normalized);
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

            if (!IsWorkspace(candidate.Workspace, workspace))
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

    internal IReadOnlyList<SessionSummary> List(string? workspaceRoot = null)
    {
        var workspace = string.IsNullOrWhiteSpace(workspaceRoot)
            ? null
            : Path.GetFullPath(workspaceRoot);
        var sessions = new List<SessionSummary>();
        foreach (var path in EnumerateFiles())
        {
            if (!TryRead(path, out var document)
                || string.IsNullOrWhiteSpace(document.Workspace)
                || document.Items.Count == 0
                || (workspace is not null && !IsWorkspace(document.Workspace, workspace)))
            {
                continue;
            }

            sessions.Add(
                new SessionSummary(
                    document.Id!,
                    document.Workspace,
                    document.PlanMode,
                    document.CreatedUtc,
                    document.UpdatedUtc,
                    Math.Max(0, document.UserTurns),
                    FirstUserText(document)));
        }

        return sessions
            .OrderByDescending(session => session.UpdatedUtc ?? DateTimeOffset.MinValue)
            .ThenBy(session => session.Id, StringComparer.Ordinal)
            .ToArray();
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
            if (parsed is null
                || string.IsNullOrWhiteSpace(parsed.Id)
                || !IsValidId(parsed.Id.Trim())
                || parsed.Items is null
                || parsed.Todos is null)
            {
                return false;
            }

            parsed.Id = parsed.Id.Trim();
            document = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string FirstUserText(SessionDocument document)
    {
        var text = document.Items.FirstOrDefault(
            item => string.Equals(item.Kind, "message", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase))?.Text;
        return string.IsNullOrWhiteSpace(text)
            ? "compacted conversation"
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsWorkspace(string candidate, string workspace)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(candidate),
                workspace,
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsValidId(string id) =>
        id.Length > 0
        && id is not "." and not ".."
        && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !id.Contains('/')
        && !id.Contains('\\');

    private string PathFor(string id) =>
        Path.Combine(_home.SessionsDirectory, id + ".json");
}
