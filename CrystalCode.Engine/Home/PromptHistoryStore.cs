using System.Text;
using System.Text.Json;

using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Home;

internal sealed class PromptHistoryStore
{
    private const int MaximumEntries = 200;
    private static readonly JsonSerializerOptions HistoryJson = new(HomeJson.Options)
    {
        WriteIndented = false
    };
    private readonly CrystalHome _home;
    private readonly string _workspace;

    public PromptHistoryStore(CrystalHome home, string workspace)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        _home = home;
        _workspace = Canonical(workspace);
    }

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken)
    {
        var entries = await ReadAsync(cancellationToken);
        return entries
            .Where(entry => string.Equals(Canonical(entry.Workspace), _workspace, StringComparison.Ordinal))
            .Select(entry => entry.Text)
            .ToArray();
    }

    public async Task AppendAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text) || text.Contains(ImageMarkerText.Prefix))
        {
            return;
        }

        var entries = (await ReadAsync(cancellationToken)).ToList();
        if (entries.Count > 0
            && string.Equals(Canonical(entries[^1].Workspace), _workspace, StringComparison.Ordinal)
            && string.Equals(entries[^1].Text, text, StringComparison.Ordinal))
        {
            return;
        }

        entries.Add(new PromptHistoryDocument(_workspace, text));
        TrimEachWorkspace(entries, MaximumEntries);

        _home.EnsureCreated();
        var lines = new StringBuilder();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines.Append(JsonSerializer.Serialize(entry, HistoryJson)).Append(Environment.NewLine);
        }

        AtomicFile.WriteText(_home.PromptHistoryPath, lines.ToString(), ownerOnly: true);
    }

    private async Task<IReadOnlyList<PromptHistoryDocument>> ReadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_home.PromptHistoryPath))
        {
            return [];
        }

        var entries = new List<PromptHistoryDocument>();
        foreach (var line in await File.ReadAllLinesAsync(_home.PromptHistoryPath, cancellationToken))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<PromptHistoryDocument>(line, HistoryJson);
                if (entry is not null
                    && !string.IsNullOrWhiteSpace(entry.Workspace)
                    && !string.IsNullOrWhiteSpace(entry.Text)
                    && !entry.Text.Contains(ImageMarkerText.Prefix))
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Ignore a damaged line while retaining valid history.
            }
        }

        return entries;
    }

    private static void TrimEachWorkspace(List<PromptHistoryDocument> entries, int maximum)
    {
        var kept = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var workspace = Canonical(entries[index].Workspace);
            kept.TryGetValue(workspace, out var count);
            count++;
            if (count > maximum)
            {
                entries.RemoveAt(index);
                continue;
            }

            kept[workspace] = count;
        }
    }

    private static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            return Workspace.Canonicalize(full);
        }
        catch (Exception exception) when (exception is IOException
            or ArgumentException
            or NotSupportedException)
        {
            return full;
        }
    }
}
