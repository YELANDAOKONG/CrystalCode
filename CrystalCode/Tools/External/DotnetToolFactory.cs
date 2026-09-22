using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

using Crystal.Multimodal.Tools;
using Crystal.Tools;

namespace CrystalCode.Tools.External;

/// <summary>
/// Loads every public <see cref="ITool"/> or <see cref="IMultimodalTool"/>
/// from one framework-dependent assembly.
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
        List<IMultimodalTool> planMultimodal,
        List<IMultimodalTool> workMultimodal,
        Dictionary<string, ExternalToolSpec> classifications,
        Dictionary<string, ParsedToolSet> origins)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(registered);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(planMultimodal);
        ArgumentNullException.ThrowIfNull(workMultimodal);
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
        var loaded = new List<(
            ITool? Text,
            IMultimodalTool? Multimodal,
            ExternalToolSpec Spec)>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var types = SelectTypes(exported, set.Types, set.DirectoryName, notes);
        if (types is null)
        {
            return false;
        }

        foreach (var type in types)
        {
            if (!TryCreateTool(
                    type,
                    set.DirectoryName,
                    notes,
                    out var textTool,
                    out var multimodalTool))
            {
                return false;
            }

            var definition = textTool?.Definition ?? multimodalTool!.Definition;
            if (textTool is not null
                && multimodalTool is not null
                && !DefinitionsMatch(textTool.Definition, multimodalTool.Definition))
            {
                notes.Add(
                    $"External tool set '{set.DirectoryName}' was skipped: '{type.FullName}' exposes different text and multimodal definitions.");
                return false;
            }

            var name = definition.Name;
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
                spec = WithDefinition(definition, overlay);
            }
            else
            {
                spec = new ExternalToolSpec(
                    name,
                    definition.Description ?? name,
                    definition.InputSchema,
                    set.Catalogs,
                    approval: set.Approval);
            }

            loaded.Add((textTool, multimodalTool, spec));
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
                $"External tool set '{set.DirectoryName}' was skipped: no public ITool or IMultimodalTool types. Exported types: {listed}.");
            return false;
        }

        foreach (var pair in loaded)
        {
            registered.Add(pair.Spec.Name);
            if (pair.Text is not null)
            {
                var wrapped = new FencedExternalTool(
                    pair.Text,
                    workspace,
                    pair.Spec.PathArguments);
                AddText(pair.Spec, wrapped, plan, work);
            }

            if (pair.Multimodal is not null)
            {
                var wrapped = new FencedExternalMultimodalTool(
                    pair.Multimodal,
                    workspace,
                    pair.Spec.PathArguments);
                AddMultimodal(
                    pair.Spec,
                    wrapped,
                    planMultimodal,
                    workMultimodal);
            }

            classifications[pair.Spec.Name] = pair.Spec;
            origins[pair.Spec.Name] = set;
        }

        return true;
    }

    private static bool TryCreateTool(
        Type type,
        string directoryName,
        IList<string> notes,
        out ITool? textTool,
        out IMultimodalTool? multimodalTool)
    {
        textTool = null;
        multimodalTool = null;
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

        textTool = instance as ITool;
        multimodalTool = instance as IMultimodalTool;
        if (textTool is null && multimodalTool is null)
        {
            notes.Add(
                $"External tool set '{directoryName}' was skipped: '{type.FullName}' is not an ITool or IMultimodalTool.");
            return false;
        }

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
            if (type is null || !IsToolType(type, []))
            {
                notes.Add(
                    $"External tool set '{directoryName}' was skipped: type '{typeName}' was not found.");
                return null;
            }

            selected.Add(type);
        }

        return selected;
    }

    private static ExternalToolSpec WithDefinition(
        ToolDefinition definition,
        ExternalToolSpec overlay) =>
        new(
            definition.Name,
            definition.Description ?? overlay.Description,
            definition.InputSchema,
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

        if (!typeof(ITool).IsAssignableFrom(type)
            && !typeof(IMultimodalTool).IsAssignableFrom(type))
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

    private static bool DefinitionsMatch(ToolDefinition left, ToolDefinition right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal)
        && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
        && JsonElement.DeepEquals(left.InputSchema, right.InputSchema);

    private static void AddText(
        ExternalToolSpec spec,
        ITool tool,
        List<ITool> plan,
        List<ITool> work)
    {
        if (spec.Catalogs.Plan)
        {
            plan.Add(tool);
        }

        if (spec.Catalogs.Work)
        {
            work.Add(tool);
        }
    }

    private static void AddMultimodal(
        ExternalToolSpec spec,
        IMultimodalTool tool,
        List<IMultimodalTool> plan,
        List<IMultimodalTool> work)
    {
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
