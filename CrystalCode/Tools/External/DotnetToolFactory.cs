using System.Collections.Concurrent;
using System.Reflection;

using Crystal.Tools;

namespace CrystalCode.Tools.External;

/// <summary>
/// Loads every public <see cref="ITool"/> from one framework-dependent assembly.
/// </summary>
internal static class DotnetToolFactory
{
    private static readonly ConcurrentBag<ExternalLoadContext> Contexts = [];

    public static bool TryCreate(
        Workspace workspace,
        ParsedToolSet set,
        HashSet<string> registered,
        IList<string> notes,
        List<ITool> plan,
        List<ITool> work,
        Dictionary<string, ExternalToolSpec> classifications,
        Dictionary<string, ParsedToolSet> origins)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(registered);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(classifications);
        ArgumentNullException.ThrowIfNull(origins);

        if (!TryResolveAssembly(set, out var assemblyPath, out var error))
        {
            notes.Add($"External tool set '{set.DirectoryName}' was skipped: {error}");
            return false;
        }

        Assembly assembly;
        try
        {
            var context = new ExternalLoadContext(assemblyPath);
            Contexts.Add(context);
            assembly = context.LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception exception)
        {
            notes.Add(
                $"External tool set '{set.DirectoryName}' was skipped: {exception.Message}");
            return false;
        }

        Type[] exported;
        try
        {
            exported = assembly.GetExportedTypes();
        }
        catch (Exception exception)
        {
            notes.Add(
                $"External tool set '{set.DirectoryName}' was skipped: {exception.Message}");
            return false;
        }

        var overlays = set.Tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var loaded = new List<(ITool Tool, ExternalToolSpec Spec)>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var types = SelectTypes(exported, set.Types, set.DirectoryName, notes);
        if (types is null)
        {
            return false;
        }

        foreach (var type in types)
        {
            if (!TryCreateTool(type, set.DirectoryName, notes, out var tool))
            {
                return false;
            }

            var name = tool.Definition.Name;
            if (!ExternalToolNames.IsToolName(name))
            {
                notes.Add(
                    $"External tool '{name}' was omitted because the name is reserved or invalid.");
                overlays.Remove(name);
                continue;
            }

            if (registered.Contains(name))
            {
                notes.Add(
                    $"External tool '{name}' was omitted because the name is already registered.");
                overlays.Remove(name);
                continue;
            }

            if (!claimed.Add(name))
            {
                notes.Add(
                    $"External tool set '{set.DirectoryName}' was skipped: duplicate tool '{name}'.");
                return false;
            }

            ExternalToolSpec spec;
            if (overlays.Remove(name, out var overlay))
            {
                spec = WithDefinition(tool, overlay);
            }
            else
            {
                spec = new ExternalToolSpec(
                    name,
                    tool.Definition.Description ?? name,
                    tool.Definition.InputSchema,
                    set.Catalogs,
                    approval: set.Approval);
            }

            loaded.Add((tool, spec));
        }

        if (overlays.Count > 0)
        {
            notes.Add(
                $"External tool set '{set.DirectoryName}' was skipped: overlay '{overlays.Keys.First()}' does not match a loaded tool.");
            return false;
        }

        if (loaded.Count == 0)
        {
            var listed = exported.Length == 0
                ? "none"
                : string.Join(", ", exported.Select(type => type.FullName ?? type.Name));
            notes.Add(
                $"External tool set '{set.DirectoryName}' was skipped: no public ITool types. Exported types: {listed}.");
            return false;
        }

        foreach (var pair in loaded)
        {
            registered.Add(pair.Spec.Name);
            var wrapped = new FencedExternalTool(pair.Tool, workspace, pair.Spec.PathArguments);
            Add(set, pair.Spec, wrapped, plan, work, classifications, origins);
        }

        return true;
    }

    private static bool TryCreateTool(
        Type type,
        string directoryName,
        IList<string> notes,
        out ITool tool)
    {
        tool = null!;
        object? instance;
        try
        {
            instance = Activator.CreateInstance(type);
        }
        catch (Exception exception)
        {
            var detail = exception is TargetInvocationException { InnerException: { } inner }
                ? inner.Message
                : exception.Message;
            notes.Add(
                $"External tool set '{directoryName}' was skipped: '{type.FullName}' could not be created: {detail}");
            return false;
        }

        if (instance is not ITool created)
        {
            notes.Add(
                $"External tool set '{directoryName}' was skipped: '{type.FullName}' is not an ITool.");
            return false;
        }

        tool = created;
        return true;
    }

    private static List<Type>? SelectTypes(
        Type[] exported,
        IReadOnlyList<string> allowlist,
        string directoryName,
        IList<string> notes)
    {
        if (allowlist.Count == 0)
        {
            return [.. exported.Where(type => IsToolType(type, []))];
        }

        var selected = new List<Type>();
        foreach (var typeName in allowlist)
        {
            var type = exported.FirstOrDefault(candidate =>
                string.Equals(candidate.FullName, typeName, StringComparison.Ordinal)
                || string.Equals(candidate.Name, typeName, StringComparison.Ordinal));
            if (type is null || !typeof(ITool).IsAssignableFrom(type) || !type.IsClass || type.IsAbstract)
            {
                notes.Add(
                    $"External tool set '{directoryName}' was skipped: type '{typeName}' was not found.");
                return null;
            }

            selected.Add(type);
        }

        return selected;
    }

    private static ExternalToolSpec WithDefinition(ITool tool, ExternalToolSpec overlay) =>
        new(
            tool.Definition.Name,
            tool.Definition.Description ?? overlay.Description,
            tool.Definition.InputSchema,
            overlay.Catalogs,
            overlay.CommandSuffix,
            overlay.Argv,
            overlay.PathArguments,
            overlay.Approval);

    private static bool IsToolType(Type type, IReadOnlyList<string> allowlist)
    {
        if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
        {
            return false;
        }

        if (!typeof(ITool).IsAssignableFrom(type))
        {
            return false;
        }

        if (allowlist.Count == 0)
        {
            return true;
        }

        foreach (var name in allowlist)
        {
            if (string.Equals(type.FullName, name, StringComparison.Ordinal)
                || string.Equals(type.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveAssembly(
        ParsedToolSet set,
        out string assemblyPath,
        out string error)
    {
        assemblyPath = string.Empty;
        error = string.Empty;
        var assembly = set.Assembly;
        if (string.IsNullOrWhiteSpace(assembly))
        {
            error = "dotnet runner requires assembly.";
            return false;
        }

        string full;
        try
        {
            full = Path.IsPathRooted(assembly)
                ? Path.GetFullPath(assembly)
                : Path.GetFullPath(Path.Combine(set.Directory, assembly));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            error = "Assembly path is not valid.";
            return false;
        }

        if (!ExternalPath.IsInside(set.Directory, full))
        {
            error = "Assembly path must stay inside the tool set directory.";
            return false;
        }

        if (!File.Exists(full))
        {
            error = $"Assembly not found: {Path.GetFileName(full)}.";
            return false;
        }

        assemblyPath = full;
        return true;
    }

    private static void Add(
        ParsedToolSet set,
        ExternalToolSpec spec,
        ITool tool,
        List<ITool> plan,
        List<ITool> work,
        Dictionary<string, ExternalToolSpec> classifications,
        Dictionary<string, ParsedToolSet> origins)
    {
        classifications[spec.Name] = spec;
        origins[spec.Name] = set;
        if (spec.Catalogs.Plan)
        {
            plan.Add(tool);
        }

        if (spec.Catalogs.Work)
        {
            work.Add(tool);
        }
    }
}
