using Crystal.Tools;
using CrystalCode.Home;
using CrystalCode.Plugins.Interfaces;

namespace CrystalCode.Tools.External;

/// <summary>
/// Loaded external tools for one workspace, split into Plan and Work lists.
/// </summary>
public sealed class ExternalCatalog
{
    public static ExternalCatalog Empty { get; } = new(
        [],
        [],
        [],
        new ExternalApprovalClassifier(new Dictionary<string, ExternalToolSpec>(StringComparer.Ordinal)));

    private ExternalCatalog(
        IReadOnlyList<ITool> plan,
        IReadOnlyList<ITool> work,
        IReadOnlyList<string> notes,
        IApprovalClassifier classifier)
    {
        PlanTools = plan;
        WorkTools = work;
        Notes = notes;
        Classifier = classifier;
    }

    public IReadOnlyList<ITool> PlanTools { get; }

    public IReadOnlyList<ITool> WorkTools { get; }

    public IReadOnlyList<string> Notes { get; }

    public IApprovalClassifier Classifier { get; }

    public static ExternalCatalog Load(
        CrystalHome home,
        Workspace workspace,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(workspace);
        if (!enabled)
        {
            return Empty;
        }

        var notes = new List<string>();
        var discovery = new ToolSetDiscovery(home);
        var sets = discovery.Collect(workspace.Root, notes);
        var registered = new HashSet<string>(StringComparer.Ordinal);
        var plan = new List<ITool>();
        var work = new List<ITool>();
        var classifications = new Dictionary<string, ExternalToolSpec>(StringComparer.Ordinal);
        foreach (var set in sets)
        {
            if (set.Runner == ExternalRunnerKind.Exec)
            {
                AddExec(workspace, set, registered, notes, plan, work, classifications);
                continue;
            }

            _ = DotnetToolFactory.TryCreate(
                workspace,
                set,
                registered,
                notes,
                plan,
                work,
                classifications);
        }

        return new ExternalCatalog(
            plan,
            work,
            notes,
            new ExternalApprovalClassifier(classifications));
    }

    private static void AddExec(
        Workspace workspace,
        ParsedToolSet set,
        HashSet<string> registered,
        IList<string> notes,
        List<ITool> plan,
        List<ITool> work,
        Dictionary<string, ExternalToolSpec> classifications)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in set.Tools)
        {
            if (!names.Add(spec.Name))
            {
                notes.Add(
                    $"External tool set '{set.DirectoryName}' was skipped: duplicate tool '{spec.Name}'.");
                return;
            }
        }

        foreach (var spec in set.Tools)
        {
            if (!registered.Add(spec.Name))
            {
                notes.Add(
                    $"External tool '{spec.Name}' was omitted because the name is already registered.");
                continue;
            }

            var wrapped = new FencedExternalTool(
                new ExecExternalTool(workspace, set, spec),
                workspace,
                spec.PathArguments);
            classifications[spec.Name] = spec;
            if (spec.Catalogs.Plan)
            {
                plan.Add(wrapped);
            }

            if (spec.Catalogs.Work)
            {
                work.Add(wrapped);
            }
        }
    }
}
