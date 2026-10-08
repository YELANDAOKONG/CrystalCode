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
        return Resolve(workspaceRoot).Prompts;
    }

    public string LoadTopicNaming(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return Resolve(workspaceRoot).Prompts.Topic;
    }

    /// <summary>
    /// True for the default set, and for a home prompt-set directory with a
    /// readable <c>prompt.json</c> and at least one prompt file. Disabled sets
    /// still count. A missing name does not fall back to the default set.
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
        string? promptSetOverride = null,
        bool usePromptAttachments = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var project = new CrystalHome(Path.Combine(workspaceRoot, ProjectDirectoryName));
        var notes = new List<string>();
        var catalog = new PromptSetDiscovery(_home).Collect(notes);
        var selection = SelectSet(catalog, promptSetOverride, notes);
        var work = ResolveNamed(PromptNames.Work, WorkPrompt.Text, selection.Selected, project);
        var plan = ResolveNamed(PromptNames.Plan, PlanPrompt.Text, selection.Selected, project);
        var review = ResolveNamed(
            PromptNames.Review,
            ApprovalReviewPrompt.SystemText,
            selection.Selected,
            project);
        var reviewUser = ResolveNamed(
            PromptNames.ReviewUser,
            ApprovalReviewPrompt.UserTemplate,
            selection.Selected,
            project);
        var topic = ResolveNamed(
            PromptNames.Topic,
            TopicNamingPrompt.Text,
            selection.Selected,
            project);
        var compactionSystem = ResolveNamed(
            PromptNames.CompactionSystem,
            CompactionPrompt.SystemText,
            selection.Selected,
            project);
        var compactionUser = ResolveNamed(
            PromptNames.CompactionUser,
            CompactionPrompt.UserTemplate,
            selection.Selected,
            project);
        var imageSystem = ResolveNamed(
            PromptNames.ImageSystem,
            ImageDescriptionPrompt.SystemText,
            selection.Selected,
            project);
        var imageUser = ResolveNamed(
            PromptNames.ImageUser,
            ImageDescriptionPrompt.UserTemplate,
            selection.Selected,
            project);
        var attachments = new PromptAttachmentDiscovery().Collect(_home, project, notes);
        var enabled = ResolveAttachments(attachments, usePromptAttachments, notes);
        return new PromptResolution(
            new PromptSet(
                work.Text,
                plan.Text,
                review.Text,
                ReadInstructions(workspaceRoot, project),
                enabled.Work,
                enabled.Plan,
                enabled.Review,
                topic.Text,
                reviewUser.Text,
                compactionSystem.Text,
                compactionUser.Text,
                imageSystem.Text,
                imageUser.Text),
            selection.Effective,
            catalog.Names,
            work.Source,
            plan.Source,
            review.Source,
            Dedupe(notes),
            enabled.Entries,
            selection.Entries);
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

    private static SetSelection SelectSet(
        PromptSetCatalog catalog,
        string? promptSetOverride,
        List<string> notes)
    {
        var enabled = new List<PromptSetDefinition>();
        foreach (var name in catalog.Names)
        {
            if (catalog.TryGet(name, out var definition) && definition.Manifest.Enabled)
            {
                enabled.Add(definition);
            }
        }

        PromptSetDefinition? selected = null;
        var effective = PromptSetNames.Default;
        if (promptSetOverride is not null)
        {
            var normalized = promptSetOverride.Trim();
            if (!string.Equals(normalized, PromptSetNames.Default, StringComparison.Ordinal))
            {
                if (catalog.TryGet(normalized, out var found))
                {
                    selected = found;
                    effective = found.Name;
                }
                else
                {
                    notes.Add($"Prompt set '{normalized}' was not found; using the default prompt set.");
                }
            }
        }
        else if (enabled.Count == 1)
        {
            selected = enabled[0];
            effective = enabled[0].Name;
        }
        else if (enabled.Count > 1)
        {
            notes.Add("More than one prompt set is enabled; using the default prompt set.");
        }

        var entries = new List<PromptSetEntry>();
        foreach (var name in catalog.Names)
        {
            if (!catalog.TryGet(name, out var definition))
            {
                continue;
            }

            entries.Add(new PromptSetEntry(
                name,
                PromptManifestDirectory.Title(name, definition.Manifest),
                definition.Manifest.Description,
                definition.Manifest.Enabled,
                string.Equals(name, effective, StringComparison.Ordinal)));
        }

        return new SetSelection(selected, effective, entries);
    }

    private static AttachmentSelection ResolveAttachments(
        PromptAttachmentCatalog catalog,
        bool usePromptAttachments,
        List<string> notes)
    {
        if (!usePromptAttachments)
        {
            notes.Add("Prompt attachments are off for this process.");
        }

        var definitions = new List<PromptAttachmentDefinition>();
        foreach (var name in catalog.Names)
        {
            if (catalog.TryGet(name, out var definition))
            {
                definitions.Add(definition);
            }
        }

        var active = definitions
            .Where(item => usePromptAttachments && item.Manifest.Enabled)
            .OrderBy(item => item.Manifest.Order ?? int.MaxValue)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var definition in active)
        {
            if (definition.ReplacedHome)
            {
                notes.Add($"Prompt attachment '{definition.Name}' uses the workspace copy.");
            }
        }

        var work = new List<string>();
        var plan = new List<string>();
        var review = new List<string>();
        foreach (var definition in active)
        {
            AddNamed(work, definition.Directory, PromptNames.Work);
            AddNamed(plan, definition.Directory, PromptNames.Plan);
            AddNamed(review, definition.Directory, PromptNames.Review);
        }

        var activeNames = new HashSet<string>(active.Select(item => item.Name), StringComparer.Ordinal);
        var entries = new List<PromptAttachmentEntry>();
        foreach (var definition in active)
        {
            entries.Add(Entry(definition, effective: true));
        }

        foreach (var definition in definitions.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (activeNames.Contains(definition.Name))
            {
                continue;
            }

            entries.Add(Entry(definition, effective: false));
        }

        return new AttachmentSelection(work, plan, review, entries);
    }

    private static PromptAttachmentEntry Entry(PromptAttachmentDefinition definition, bool effective) =>
        new(
            definition.Name,
            PromptManifestDirectory.Title(definition.Name, definition.Manifest),
            definition.Manifest.Description,
            definition.Source,
            definition.Manifest.Enabled,
            effective,
            definition.Manifest.Order);

    private static void AddNamed(List<string> parts, string directory, string name)
    {
        var text = PromptFiles.ReadAttachment(directory, name);
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

    private sealed record SetSelection(
        PromptSetDefinition? Selected,
        string Effective,
        IReadOnlyList<PromptSetEntry> Entries);

    private sealed record AttachmentSelection(
        IReadOnlyList<string> Work,
        IReadOnlyList<string> Plan,
        IReadOnlyList<string> Review,
        IReadOnlyList<PromptAttachmentEntry> Entries);
}
