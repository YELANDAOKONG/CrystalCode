using CrystalCode.Home;

namespace CrystalCode.Prompts;

/// <summary>
/// Resolves built-ins, a selected Home prompt set, direct Home and project
/// overrides, then appended instructions, including
/// OpenCode-compatible <c>AGENTS.md</c> / <c>CLAUDE.md</c>, are appended.
/// </summary>
public sealed class PromptStore
{
    public const string ProjectDirectoryName = ".crystal";

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
        return Resolve(workspaceRoot, PromptSetNames.Default).Prompts;
    }

    internal PromptResolution Resolve(string workspaceRoot, string selectedSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedSet);
        var project = new CrystalHome(Path.Combine(workspaceRoot, ProjectDirectoryName));
        var notes = new List<string>();
        var catalog = new PromptSetDiscovery(_home).Collect(notes);
        var normalized = selectedSet.Trim();
        PromptSetDefinition? selected = null;
        var effectiveSet = PromptSetNames.Default;
        if (!string.Equals(normalized, PromptSetNames.Default, StringComparison.Ordinal))
        {
            if (catalog.TryGet(normalized, out var found))
            {
                selected = found;
                effectiveSet = found.Name;
            }
            else
            {
                notes.Add($"Prompt set '{normalized}' was not found; using the default prompt set.");
            }
        }

        var work = ResolveNamed(PromptNames.Work, WorkPrompt.Text, selected, project);
        var plan = ResolveNamed(PromptNames.Plan, PlanPrompt.Text, selected, project);
        var review = ResolveNamed(
            PromptNames.Review,
            ApprovalReviewPrompt.SystemText,
            selected,
            project);
        return new PromptResolution(
            new PromptSet(work.Text, plan.Text, review.Text, ReadInstructions(workspaceRoot, project)),
            effectiveSet,
            catalog.Names,
            work.Source,
            plan.Source,
            review.Source,
            [.. notes]);
    }

    private (string Text, PromptSource Source) ResolveNamed(
        string name,
        string builtIn,
        PromptSetDefinition? selected,
        CrystalHome project)
    {
        var text = builtIn;
        var source = PromptSource.BuiltIn;
        var fromSet = selected is null ? null : PromptFiles.ReadNamed(selected.Directory, name);
        if (fromSet is not null)
        {
            text = fromSet;
            source = PromptSource.PromptSet;
        }

        var fromHome = PromptFiles.ReadNamed(_home.PromptsDirectory, name);
        if (fromHome is not null)
        {
            text = fromHome;
            source = PromptSource.HomeOverride;
        }

        var fromProject = PromptFiles.ReadNamed(project.PromptsDirectory, name);
        if (fromProject is not null)
        {
            text = fromProject;
            source = PromptSource.ProjectOverride;
        }

        return (text, source);
    }

    private string ReadInstructions(string workspaceRoot, CrystalHome project)
    {
        var parts = new List<string>();
        AddNamedFile(parts, _home.Root, "instructions");
        AddNamedFile(parts, project.Root, "instructions");
        AddIfPresent(parts, Path.Combine(workspaceRoot, ".crystal.md"));
        parts.AddRange(_discovery.Collect(workspaceRoot));
        return string.Join("\n\n", parts);
    }

    private static void AddNamedFile(List<string> parts, string directory, string name)
    {
        var text = PromptFiles.ReadNamed(directory, name);
        if (text is not null)
        {
            parts.Add(text);
        }
    }

    private static void AddIfPresent(List<string> parts, string path)
    {
        if (PromptFiles.TryRead(path, out var text))
        {
            parts.Add(text);
        }
    }
}
