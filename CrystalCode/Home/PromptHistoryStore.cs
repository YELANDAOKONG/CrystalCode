using System.Text.Json;

namespace CrystalCode.Home;

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
        _workspace = Path.GetFullPath(workspace);
    }

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken)
    {
        var entries = await ReadAsync(cancellationToken);
        return entries
            .Where(entry => string.Equals(entry.Workspace, _workspace, StringComparison.Ordinal))
            .Select(entry => entry.Text)
            .ToArray();
    }

    public async Task AppendAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text) || text.Contains("[Image #", StringComparison.Ordinal))
        {
            return;
        }

        var entries = (await ReadAsync(cancellationToken)).ToList();
        if (entries.Count > 0
            && string.Equals(entries[^1].Workspace, _workspace, StringComparison.Ordinal)
            && string.Equals(entries[^1].Text, text, StringComparison.Ordinal))
        {
            return;
        }

        entries.Add(new PromptHistoryDocument(_workspace, text));
        if (entries.Count > MaximumEntries)
        {
            entries.RemoveRange(0, entries.Count - MaximumEntries);
        }

        _home.EnsureCreated();
        var temporaryPath = _home.PromptHistoryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var stream = new FileStream(temporaryPath, options))
            await using (var writer = new StreamWriter(stream))
            {
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(JsonSerializer.Serialize(entry, HistoryJson));
                }
            }

            File.Move(temporaryPath, _home.PromptHistoryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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
                    && !entry.Text.Contains("[Image #", StringComparison.Ordinal))
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Ignore a damaged line while retaining valid history.
            }
        }

        return entries.TakeLast(MaximumEntries).ToArray();
    }
}
