using Crystal.Tools;
using CrystalCode.Configuration;
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
        new ExternalApprovalClassifier(new Dictionary<string, ExternalToolSpec>(StringComparer.Ordinal)),
        new HashSet<string>(StringComparer.Ordinal));

    private ExternalCatalog(
        IReadOnlyList<ITool> plan,
        IReadOnlyList<ITool> work,
        IReadOnlyList<string> notes,
        IApprovalClassifier classifier,
        IReadOnlySet<string> automaticTools)
    {
        PlanTools = plan;
        WorkTools = work;
        Notes = notes;
        Classifier = classifier;
        AutomaticTools = automaticTools;
    }

    public IReadOnlyList<ITool> PlanTools { get; }

    public IReadOnlyList<ITool> WorkTools { get; }

    public IReadOnlyList<string> Notes { get; }

    public IApprovalClassifier Classifier { get; }

    public IReadOnlyList<ExternalToolInfo> Tools { get; private init; } = [];

    public IReadOnlySet<string> AutomaticTools { get; }

    public static ExternalCatalog Load(
        CrystalHome home,
        Workspace workspace,
        bool enabled,
        ExternalToolApprovalSettings? approvalSettings = null)
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
        var origins = new Dictionary<string, ParsedToolSet>(StringComparer.Ordinal);
        foreach (var set in sets)
        {
            if (set.Runner == ExternalRunnerKind.Exec)
            {
                AddExec(
                    workspace,
                    set,
                    registered,
                    notes,
                    plan,
                    work,
                    classifications,
                    origins);
                continue;
            }

            _ = DotnetToolFactory.TryCreate(
                workspace,
                set,
                registered,
                notes,
                plan,
                work,
                classifications,
                origins);
        }

        var settings = approvalSettings ?? ExternalToolApprovalSettings.Default;
        var tools = new List<ExternalToolInfo>();
        var automaticTools = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in classifications.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var set = origins[pair.Key];
            var declared = pair.Value.Approval;
            var trust = set.Source == ExternalToolSource.Home
                ? settings.Home
                : settings.Project;
            var effective = trust == ExternalToolTrustPolicy.Author
                ? declared
                : ExternalApprovalMode.Inherit;
            if (effective == ExternalApprovalMode.Always)
            {
                automaticTools.Add(pair.Key);
            }

            tools.Add(
                new ExternalToolInfo(
                    pair.Key,
                    set.DirectoryName,
                    set.Source,
                    pair.Value.Catalogs,
                    declared,
                    effective));
        }

        return new ExternalCatalog(
            plan,
            work,
            notes,
            new ExternalApprovalClassifier(classifications),
            automaticTools)
        {
            Tools = tools
        };
    }

    private static void AddExec(
        Workspace workspace,
        ParsedToolSet set,
        HashSet<string> registered,
        IList<string> notes,
        List<ITool> plan,
        List<ITool> work,
        Dictionary<string, ExternalToolSpec> classifications,
        Dictionary<string, ParsedToolSet> origins)
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
            origins[spec.Name] = set;
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
