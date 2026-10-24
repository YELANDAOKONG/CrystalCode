using System.Text;
using System.Text.Json;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Modes;

/// <summary>
/// Loads enabled custom modes from <c>modes/&lt;name&gt;</c> under Home and
/// the workspace <c>.crystal</c> directory. A workspace entry replaces the
/// Home entry of the same name as a whole, even when it is disabled or invalid.
/// </summary>
internal sealed class ModeCatalog
{
    public const string Plan = "plan";
    public const string Work = "work";
    private const string DefaultName = "default";
    private const int MaximumDirectories = 256;
    private const long MaximumFileBytes = 64 * 1024;

    private ModeCatalog(IEnumerable<ModeDefinition> definitions, IEnumerable<string> notes)
    {
        Definitions = definitions.OrderBy(static mode => mode.Name, StringComparer.Ordinal).ToArray();
        Notes = notes.ToArray();
    }

    public static ModeCatalog Empty { get; } = new([], []);

    public IReadOnlyList<ModeDefinition> Definitions { get; }

    public IReadOnlyList<string> Notes { get; }

    public static bool IsReserved(string name) =>
        name.Equals(Plan, StringComparison.OrdinalIgnoreCase)
        || name.Equals(Work, StringComparison.OrdinalIgnoreCase)
        || name.Equals(DefaultName, StringComparison.OrdinalIgnoreCase);

    public ModeDefinition? Find(string name) =>
        Definitions.FirstOrDefault(mode => mode.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static ModeCatalog Load(CrystalHome home, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var merged = new Dictionary<string, ModeDefinition>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        foreach (var root in new[]
        {
            Path.Combine(home.Root, "modes"),
            Path.Combine(workspaceRoot, ".crystal", "modes")
        })
        {
            LoadRoot(root, merged, notes);
        }

        return new ModeCatalog(merged.Values, notes);
    }

    private static void LoadRoot(
        string root,
        Dictionary<string, ModeDefinition> merged,
        List<string> notes)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(root)
                         .Order(StringComparer.Ordinal)
                         .Take(MaximumDirectories))
            {
                var name = Path.GetFileName(directory);
                // A skipped entry still replaces the Home definition of that name.
                merged.Remove(name);
                try
                {
                    if (IsReserved(name))
                    {
                        throw new InvalidDataException("The name is reserved.");
                    }

                    if (!ExternalToolNames.IsDirectoryName(name)
                        || new DirectoryInfo(directory).LinkTarget is not null)
                    {
                        throw new InvalidDataException("Invalid mode directory or symbolic link.");
                    }

                    var manifest = JsonSerializer.Deserialize<ModeManifest>(
                            ReadFile(Path.Combine(directory, "mode.json")),
                            HomeJson.Options)
                        ?? throw new InvalidDataException("The mode manifest is empty.");
                    if (!manifest.Enabled)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(manifest.Description))
                    {
                        throw new InvalidDataException("Mode description is required.");
                    }

                    if (manifest.Tools is null)
                    {
                        throw new InvalidDataException("Mode tools are required.");
                    }

                    var tools = ReadTools(manifest.Tools);
                    var prompt = ReadFile(Path.Combine(directory, "prompt.md")).Trim();
                    if (prompt.Length == 0)
                    {
                        throw new InvalidDataException("Mode prompt is required.");
                    }

                    merged[name] = new ModeDefinition(name, manifest.Description.Trim(), prompt, tools);
                }
                catch (Exception exception) when (exception is IOException
                    or InvalidDataException
                    or JsonException
                    or UnauthorizedAccessException
                    or DecoderFallbackException)
                {
                    notes.Add($"Mode '{name}' skipped: {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            notes.Add($"Mode directory unavailable: {root}.");
        }
    }

    private static string[] ReadTools(IEnumerable<string> requested)
    {
        var tools = new List<string>();
        foreach (var raw in requested)
        {
            var tool = raw?.Trim() ?? string.Empty;
            if (!ExternalToolNames.IsReserved(tool))
            {
                throw new InvalidDataException($"Unknown built-in tool '{raw}'.");
            }

            if (!tools.Contains(tool, StringComparer.Ordinal))
            {
                tools.Add(tool);
            }
        }

        return [.. tools];
    }

    private static string ReadFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || file.Length > MaximumFileBytes)
        {
            throw new InvalidDataException("Mode file is missing, linked, or larger than 64 KiB.");
        }

        return File.ReadAllText(path, new UTF8Encoding(false, true));
    }
}
