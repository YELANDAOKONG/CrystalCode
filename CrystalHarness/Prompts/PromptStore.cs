using CrystalHarness.Home;

namespace CrystalHarness.Prompts;

/// <summary>
/// Loads custom prompts from <c>~/.crystal</c> then the project <c>.crystal</c>.
/// Named files replace the built-in text. Instructions, including
/// OpenCode-compatible <c>AGENTS.md</c> / <c>CLAUDE.md</c>, are appended.
/// </summary>
public sealed class PromptStore
{
    public const string ProjectDirectoryName = ".crystal";

    private static readonly string[] PromptExtensions = [".md", ".txt"];

    private readonly CrystalHome _home;
    private readonly InstructionDiscovery _discovery;

    public PromptStore(CrystalHome home, InstructionDiscovery? discovery = null)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
        _discovery = discovery ?? InstructionDiscovery.Create(home);
    }

    public PromptSet Load(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var project = new CrystalHome(Path.Combine(workspaceRoot, ProjectDirectoryName));
        return new PromptSet(
            ReadNamed(PromptNames.Work, project) ?? WorkPrompt.Text,
            ReadNamed(PromptNames.Plan, project) ?? PlanPrompt.Text,
            ReadNamed(PromptNames.Review, project) ?? ApprovalReviewPrompt.SystemText,
            ReadInstructions(workspaceRoot, project));
    }

    private string? ReadNamed(string name, CrystalHome project) =>
        ReadNamedFile(project.PromptsDirectory, name)
        ?? ReadNamedFile(_home.PromptsDirectory, name);

    private string ReadInstructions(string workspaceRoot, CrystalHome project)
    {
        var parts = new List<string>();
        AddNamedFile(parts, _home.Root, "instructions");
        AddNamedFile(parts, project.Root, "instructions");
        AddIfPresent(parts, Path.Combine(workspaceRoot, ".crystal.md"));
        parts.AddRange(_discovery.Collect(workspaceRoot));
        return string.Join("\n\n", parts);
    }

    private static string? ReadNamedFile(string directory, string name)
    {
        foreach (var extension in PromptExtensions)
        {
            var path = Path.Combine(directory, name + extension);
            if (TryRead(path, out var text))
            {
                return text;
            }
        }

        return null;
    }

    private static void AddNamedFile(List<string> parts, string directory, string name)
    {
        var text = ReadNamedFile(directory, name);
        if (text is not null)
        {
            parts.Add(text);
        }
    }

    private static void AddIfPresent(List<string> parts, string path)
    {
        if (TryRead(path, out var text))
        {
            parts.Add(text);
        }
    }

    private static bool TryRead(string path, out string text)
    {
        text = string.Empty;
        if (!File.Exists(path))
        {
            return false;
        }

        var raw = File.ReadAllText(path).Trim();
        if (raw.Length == 0)
        {
            return false;
        }

        text = raw;
        return true;
    }
}
