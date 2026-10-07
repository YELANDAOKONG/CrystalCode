using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Resolves built-ins, a selected Home prompt set, direct Home and project
/// overrides, then enabled prompt attachments. Workspace instructions, including
/// OpenCode-compatible <c>AGENTS.md</c> / <c>CLAUDE.md</c>, are appended inside
/// the body and are not prompt attachments.
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

    public string LoadTopicNaming(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var project = new CrystalHome(Path.Combine(workspaceRoot, ProjectDirectoryName));
        var text = PromptFiles.ReadNamed(_home.PromptsDirectory, PromptNames.Topic);
        var projectText = PromptFiles.ReadNamed(project.PromptsDirectory, PromptNames.Topic);
        return projectText ?? text ?? TopicNamingPrompt.Text;
    }

    /// <summary>
    /// True for the default set, and for a home prompt-set directory that
    /// contains at least one prompt file. A missing name does not fall back
    /// to the default set.
    /// </summary>
    public bool ContainsSet(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (string.Equals(normalized, PromptSetNames.Default, StringComparison.Ordinal))
        {
            return true;
        }

        if (!PromptSetNames.IsValid(normalized))
        {
            return false;
        }

        var notes = new List<string>();
        return new PromptSetDiscovery(_home).Collect(notes).TryGet(normalized, out _);
    }

    internal PromptResolution Resolve(
        string workspaceRoot,
        string selectedSet,
        IReadOnlyList<string>? promptAttachments = null)
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
        var attachments = new PromptAttachmentDiscovery().Collect(_home, project, notes);
        var enabled = ResolveAttachments(promptAttachments, attachments, notes);
        return new PromptResolution(
            new PromptSet(
                work.Text,
                plan.Text,
                review.Text,
                ReadInstructions(workspaceRoot, project),
                enabled.Work,
                enabled.Plan,
                enabled.Review),
            effectiveSet,
            catalog.Names,
            work.Source,
            plan.Source,
            review.Source,
            Dedupe(notes),
            enabled.Entries);
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

    private static AttachmentSelection ResolveAttachments(
        IReadOnlyList<string>? requested,
        PromptAttachmentCatalog catalog,
        List<string> notes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<PromptAttachmentEntry>();
        var work = new List<string>();
        var plan = new List<string>();
        var review = new List<string>();
        if (requested is not null)
        {
            foreach (var raw in requested)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var name = raw.Trim();
                if (!seen.Add(name))
                {
                    notes.Add(
                        $"Prompt attachment '{name}' is listed more than once; later copies are ignored.");
                    continue;
                }

                if (!PromptAttachmentNames.IsValid(name) || !catalog.TryGet(name, out var definition))
                {
                    if (!PromptAttachmentNames.IsValid(name))
                    {
                        notes.Add(
                            $"Prompt attachment '{name}' was skipped: directory name is invalid.");
                    }
                    else
                    {
                        notes.Add($"Prompt attachment '{name}' was not found.");
                    }

                    entries.Add(new PromptAttachmentEntry(name, null, Enabled: true));
                    continue;
                }

                if (definition.ReplacedHome)
                {
                    notes.Add($"Prompt attachment '{name}' uses the workspace copy.");
                }

                entries.Add(new PromptAttachmentEntry(name, definition.Source, Enabled: true));
                AddNamed(work, definition.Directory, PromptNames.Work);
                AddNamed(plan, definition.Directory, PromptNames.Plan);
                AddNamed(review, definition.Directory, PromptNames.Review);
            }
        }

        foreach (var name in catalog.Names)
        {
            if (seen.Contains(name) || !catalog.TryGet(name, out var definition))
            {
                continue;
            }

            entries.Add(new PromptAttachmentEntry(name, definition.Source, Enabled: false));
        }

        return new AttachmentSelection(work, plan, review, entries);
    }

    private static void AddNamed(List<string> parts, string directory, string name)
    {
        var text = PromptFiles.ReadNamed(directory, name);
        if (text is not null)
        {
            parts.Add(text);
        }
    }

    private static List<string> Dedupe(List<string> notes)
    {
        var unique = new List<string>(notes.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var note in notes)
        {
            if (seen.Add(note))
            {
                unique.Add(note);
            }
        }

        return unique;
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

    private sealed record AttachmentSelection(
        IReadOnlyList<string> Work,
        IReadOnlyList<string> Plan,
        IReadOnlyList<string> Review,
        IReadOnlyList<PromptAttachmentEntry> Entries);
}
