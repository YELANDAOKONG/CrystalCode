using System.Text.Json;

using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Home;

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
        var stored = CopyWithImages(document, PersistImages(document.Images));
        AtomicFile.WriteText(path, JsonSerializer.Serialize(stored, HomeJson.Options), ownerOnly: true);
    }

    internal IReadOnlyDictionary<int, ImageAttachment> ReadImages(
        IEnumerable<SessionImageDocument> images) =>
        SessionMapper.ReadImages(images, new ImageBlobStore(_home));

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

    public IReadOnlyList<SessionSummary> List(string? workspaceRoot = null)
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
                    FirstUserText(document),
                    document.Mode));
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
            parsed.Images ??= [];
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
        var text = FirstUserMessage(document.Archive) ?? FirstUserMessage(document.Items);
        return string.IsNullOrWhiteSpace(text)
            ? "compacted conversation"
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? FirstUserMessage(IReadOnlyList<SessionItemDocument>? items) =>
        items?.FirstOrDefault(
            item => string.Equals(item.Kind, "message", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase))?.Text;

    private static bool IsWorkspace(string candidate, string workspace)
    {
        try
        {
            return string.Equals(
                Workspace.Canonicalize(candidate),
                Workspace.Canonicalize(workspace),
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or IOException)
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

    private List<SessionImageDocument> PersistImages(IEnumerable<SessionImageDocument> images)
    {
        var store = new ImageBlobStore(_home);
        var persisted = new List<SessionImageDocument>();
        foreach (var image in images)
        {
            if (image is null
                || image.Number <= 0
                || string.IsNullOrWhiteSpace(image.MimeType))
            {
                continue;
            }

            if (image.Data is { Length: > 0 } data)
            {
                if (data.Length > ImageFile.MaximumBytes
                    || !string.Equals(
                        ImageFile.DetectMimeType(data),
                        image.MimeType,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                persisted.Add(new SessionImageDocument
                {
                    Number = image.Number,
                    MimeType = image.MimeType,
                    ContentHash = store.Store(data, image.MimeType)
                });
                continue;
            }

            persisted.Add(new SessionImageDocument
            {
                Number = image.Number,
                MimeType = image.MimeType,
                ContentHash = image.ContentHash,
                Uri = image.Uri
            });
        }

        return persisted;
    }

    private SessionDocument CopyWithImages(
        SessionDocument source,
        List<SessionImageDocument> images) => new()
    {
        Id = source.Id,
        Workspace = source.Workspace,
        PlanMode = source.PlanMode,
        Mode = source.Mode,
        Access = source.Access,
        DisabledBuiltInTools = source.DisabledBuiltInTools,
        CreatedUtc = source.CreatedUtc,
        UpdatedUtc = source.UpdatedUtc,
        Items = source.Items,
        Archive = source.Archive,
        ImageMarkersTagged = source.ImageMarkersTagged,
        Images = images,
        Todos = source.Todos,
        UserTurns = source.UserTurns,
        ModelCalls = source.ModelCalls,
        ToolCalls = source.ToolCalls,
        Usage = source.Usage,
        CumulativeUsage = source.CumulativeUsage,
        Children = source.Children.Select(child => child with
        {
            Items = child.Items.ToArray(), Archive = child.Archive.ToArray(),
            Images = PersistImages(child.Images).ToArray(),
            Usage = CloneUsage(child.Usage),
            CumulativeUsage = CloneUsage(child.CumulativeUsage),
            Notes = child.Notes.ToArray()
        }).ToList()
    };

    private static SessionUsageDocument? CloneUsage(SessionUsageDocument? usage) => usage is null
        ? null
        : new SessionUsageDocument
        {
            InputTokenCount = usage.InputTokenCount,
            OutputTokenCount = usage.OutputTokenCount,
            ReasoningTokenCount = usage.ReasoningTokenCount
        };
}
