using System.Text.Json;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Home;

internal sealed class AgentCatalog
{
    private const int MaximumFiles = 256;
    private const long MaximumFileBytes = 64 * 1024;
    private static readonly string[] Reserved = ["general", "explore", "work"];

    private AgentCatalog(IEnumerable<AgentDefinition> definitions, IEnumerable<string> notes)
    {
        Definitions = definitions.ToArray();
        Notes = notes.ToArray();
    }

    public IReadOnlyList<AgentDefinition> Definitions { get; }
    public IReadOnlyList<string> Notes { get; }

    public static AgentCatalog Load(CrystalHome home, string workspace, AgentSettings settings)
    {
        var result = new List<AgentDefinition>();
        var notes = new List<string>();
        if (!settings.Enabled)
        {
            return new AgentCatalog(result, notes);
        }
        result.Add(new("general", "Bounded general task; returns a concise report.", AgentPrompt.For("general"), settings.General));
        result.Add(new("explore", "Read-only codebase research and evidence gathering.", AgentPrompt.For("explore"), settings.Explore,
            ["read", "list", "tree", "glob", "grep", "search", "view_image", "describe_image"]));
        result.Add(new("work", "Implement a bounded change in Work mode.", AgentPrompt.For("work"), settings.Work));

        var merged = new Dictionary<string, AgentDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in new[] { Path.Combine(home.Root, "agents"), Path.Combine(workspace, ".crystal", "agents") })
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }
                foreach (var agentDirectory in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal).Take(MaximumFiles))
                {
                    var name = Path.GetFileName(agentDirectory);
                    // An invalid or disabled workspace entry still replaces the entire Home definition.
                    merged.Remove(name);
                    if (Reserved.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        notes.Add($"Agent '{name}' skipped: the name is reserved.");
                        continue;
                    }
                    try
                    {
                        if (!ExternalToolNames.IsDirectoryName(name) || new DirectoryInfo(agentDirectory).LinkTarget is not null)
                        {
                            throw new InvalidDataException("Invalid Agent directory or symbolic link.");
                        }
                        var manifestPath = Path.Combine(agentDirectory, "agent.json");
                        var manifest = JsonSerializer.Deserialize<AgentManifest>(ReadFile(manifestPath), HomeJson.Options)
                            ?? throw new InvalidDataException("The Agent manifest is empty.");
                        if (!manifest.Enabled)
                        {
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(manifest.Description))
                        {
                            throw new InvalidDataException("Agent description is required.");
                        }
                        var prompt = ReadFile(Path.Combine(agentDirectory, "prompt.md"));
                        if (string.IsNullOrWhiteSpace(prompt))
                        {
                            throw new InvalidDataException("Agent prompt is required.");
                        }
                        var tools = manifest.Tools?.Distinct(StringComparer.Ordinal).ToArray();
                        merged[name] = new(name, manifest.Description.Trim(), prompt, manifest.ModelSettings(), tools, manifestPath);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException)
                    {
                        notes.Add($"Agent '{name}' skipped: {exception.Message}");
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                notes.Add($"Agent directory unavailable: {directory}.");
            }
        }
        result.AddRange(merged.Values);
        return new AgentCatalog(result, notes);
    }

    private static string ReadFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || file.Length > MaximumFileBytes)
        {
            throw new InvalidDataException("Agent file is missing, linked, or larger than 64 KiB.");
        }
        return File.ReadAllText(path, new System.Text.UTF8Encoding(false, true));
    }
}
