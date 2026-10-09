using System.Reflection;

using Crystal.Chat;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;
using CrystalCode.Plugins.Clients;
using CrystalCode.Plugins.Data;
using CrystalCode.Plugins.Environment;
using CrystalCode.Plugins.Models;
using CrystalCode.Plugins.Hooks;
using CrystalCode.Plugins.Tools;
using CrystalCode.Tools;

using DiskContribution = CrystalCode.Plugins.PluginContribution;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>
/// Plugins loaded from <c>~/.crystal/plugins</c> and the workspace
/// <c>.crystal/plugins</c> tree.
/// </summary>
public sealed class PluginCatalog
{
    private static readonly string[] BuiltInProtocols =
    [
        "deepseek",
        "openai",
        "responses",
        "anthropic",
        "gemini",
        "ollama"
    ];

    private readonly List<IPluginClientFactory> _clients;
    private readonly IReadOnlyList<LoadedPlugin> _instances;

    private PluginCatalog(
        IReadOnlyList<ITool> planTools,
        IReadOnlyList<ITool> workTools,
        IReadOnlyList<IMultimodalTool> planMultimodalTools,
        IReadOnlyList<IMultimodalTool> workMultimodalTools,
        IReadOnlyList<IApprovalClassifier> classifiers,
        IReadOnlyList<ISlashCommand> commands,
        IReadOnlyList<IPluginHook> hooks,
        IReadOnlyList<IPluginRawHook> rawHooks,
        IReadOnlyList<IPluginClientFactory> clients,
        IReadOnlyList<PluginInfo> plugins,
        IReadOnlyList<string> notes,
        IReadOnlySet<string> toolNames,
        IReadOnlyList<LoadedPlugin> instances,
        IReadOnlyList<PluginPlaceholderRegistration> placeholders,
        IReadOnlyList<PluginActivationFailure> failures)
    {
        PlanTools = planTools;
        WorkTools = workTools;
        PlanMultimodalTools = planMultimodalTools;
        WorkMultimodalTools = workMultimodalTools;
        Classifiers = classifiers;
        Commands = commands;
        Hooks = hooks;
        RawHooks = rawHooks;
        Plugins = plugins;
        Notes = notes;
        ToolNames = toolNames;
        Placeholders = placeholders;
        Failures = failures;
        _clients = [.. clients];
        _instances = instances;
    }

    public static PluginCatalog Empty { get; } = new(
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        new HashSet<string>(StringComparer.Ordinal),
        [],
        [],
        []);

    public IReadOnlyList<ITool> PlanTools { get; }

    public IReadOnlyList<ITool> WorkTools { get; }

    public IReadOnlyList<IMultimodalTool> PlanMultimodalTools { get; }

    public IReadOnlyList<IMultimodalTool> WorkMultimodalTools { get; }

    public IReadOnlyList<IApprovalClassifier> Classifiers { get; }

    public IReadOnlyList<ISlashCommand> Commands { get; }

    public IReadOnlyList<IPluginHook> Hooks { get; }

    public IReadOnlyList<IPluginRawHook> RawHooks { get; }

    public IReadOnlyList<PluginInfo> Plugins { get; }

    public IReadOnlyList<string> Notes { get; }

    public IReadOnlySet<string> ToolNames { get; }

    public IReadOnlyList<PluginPlaceholderRegistration> Placeholders { get; }

    public IReadOnlyList<PluginActivationFailure> Failures { get; }

    public static PluginCatalog Load(
        CrystalHome home,
        Workspace workspace,
        bool enabled,
        SessionToolHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(workspace);
        if (!enabled)
        {
            return Empty;
        }

        host ??= new SessionToolHost(
            workspace,
            home,
            static () => string.Empty,
            static () => string.Empty);

        var notes = new List<string>();
        var discovered = new PluginDiscovery(home).Collect(workspace.Root, notes);
        var plan = new List<ITool>();
        var work = new List<ITool>();
        var planMultimodal = new List<IMultimodalTool>();
        var workMultimodal = new List<IMultimodalTool>();
        var classifiers = new List<IApprovalClassifier>();
        var commands = new List<ISlashCommand>();
        var hooks = new List<IPluginHook>();
        var rawHooks = new List<IPluginRawHook>();
        var clients = new List<IPluginClientFactory>();
        var plugins = new List<PluginInfo>();
        var instances = new List<LoadedPlugin>();
        var placeholders = new List<PluginPlaceholderRegistration>();
        var failures = new List<PluginActivationFailure>();
        var toolNames = new HashSet<string>(StringComparer.Ordinal);
        var commandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pluginNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placeholderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in discovered)
        {
            if (!TryActivate(item, out var plugin, out var error) || plugin is null)
            {
                Skip(item, error);
                continue;
            }

            if (string.IsNullOrWhiteSpace(plugin.Name) || !pluginNames.Add(plugin.Name.Trim()))
            {
                Skip(item, "plugin name is missing or already registered.");
                continue;
            }

            DiskContribution contribution;
            try
            {
                contribution = plugin.Contribute();
            }
            catch (Exception exception)
            {
                Skip(item, exception.Message);
                continue;
            }

            var toolCount = AddTools(
                item.DirectoryName,
                contribution,
                host,
                toolNames,
                notes,
                plan,
                work,
                planMultimodal,
                workMultimodal);
            var commandCount = AddCommands(item.DirectoryName, contribution, commandNames, notes, commands);
            var clientCount = AddClients(item.DirectoryName, contribution, notes, clients);
            var classifierCount = AddClassifiers(item.DirectoryName, contribution, notes, classifiers);
            var hookCount = AddHooks(item.DirectoryName, contribution, notes, hooks);
            var rawHookCount = AddRawHooks(item.DirectoryName, contribution, notes, rawHooks);
            AddPlaceholders(
                item.DirectoryName,
                contribution,
                placeholderNames,
                notes,
                placeholders);
            if (plugin is IPluginModelClient)
            {
                notes.Add(
                    $"Plugin '{item.DirectoryName}' can call the session and review models.");
            }

            instances.Add(new LoadedPlugin(item.DirectoryName, plugin));
            plugins.Add(new PluginInfo(
                item.DirectoryName,
                item.Source,
                plugin.Name.Trim(),
                toolCount,
                commandCount,
                hookCount,
                rawHookCount,
                clientCount,
                classifierCount));
        }

        return new PluginCatalog(
            plan,
            work,
            planMultimodal,
            workMultimodal,
            classifiers,
            commands,
            hooks,
            rawHooks,
            clients,
            plugins,
            notes,
            toolNames,
            instances,
            placeholders,
            failures);

        void Skip(DiscoveredPlugin item, string reason)
        {
            notes.Add($"Plugin '{item.DirectoryName}' was skipped: {reason}");
            failures.Add(new PluginActivationFailure(item.DirectoryName, item.Source, reason));
        }
    }

    public void Attach(IPluginEnvironment environment, Action<string> note)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(note);
        foreach (var instance in _instances)
        {
            try
            {
                instance.Plugin.Attach(environment);
            }
            catch (Exception exception)
            {
                note($"Plugin '{instance.DirectoryName}' could not be attached: {exception.Message}");
            }
        }
    }

    public void AttachSession(IPluginModels models, Action<string> note)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(note);
        foreach (var instance in _instances)
        {
            try
            {
                instance.Plugin.AttachSession(models);
            }
            catch (Exception exception)
            {
                note(
                    $"Plugin '{instance.DirectoryName}' could not receive model facts: {exception.Message}");
            }
        }
    }

    public void AttachClients(IPluginClients clients, Action<string> note)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(note);
        foreach (var instance in _instances)
        {
            if (instance.Plugin is not IPluginModelClient client)
            {
                continue;
            }

            try
            {
                client.AttachClients(clients);
            }
            catch (Exception exception)
            {
                note(
                    $"Plugin '{instance.DirectoryName}' could not receive model clients: {exception.Message}");
            }
        }
    }

    public void AttachDataDirectories(CrystalHome home, string workspaceRoot, Action<string> note)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(note);
        foreach (var instance in _instances)
        {
            if (instance.Plugin is not IPluginDataDirectory data)
            {
                continue;
            }

            try
            {
                var paths = ExtensionDataPaths.Resolve(
                    home,
                    workspaceRoot,
                    ExtensionDataKind.Plugins,
                    instance.DirectoryName);
                _ = paths.EnsureCreated();
                data.AttachDataDirectories(paths.GlobalDirectory, paths.ProjectDirectory);
            }
            catch (Exception exception)
            {
                note(
                    $"Plugin '{instance.DirectoryName}' could not receive data directories: {exception.Message}");
            }
        }
    }

    public bool TryCreateClient(HarnessSettings settings, string apiKey, out IStreamingChatClient? client)
    {
        ArgumentNullException.ThrowIfNull(settings);
        client = null;
        var protocol = settings.ActiveProvider.Protocol.Value;
        foreach (var factory in _clients)
        {
            if (!factory.CanCreate(protocol))
            {
                continue;
            }

            client = factory.Create(PluginClientRequests.From(settings, apiKey));
            return true;
        }

        return false;
    }

    public IStreamingMultimodalChatClient? CreateMultimodal(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.ActiveModel.ImageInput)
        {
            return null;
        }

        var protocol = settings.ActiveProvider.Protocol.Value;
        foreach (var factory in _clients)
        {
            if (!factory.CanCreate(protocol))
            {
                continue;
            }

            return factory.CreateMultimodal(PluginClientRequests.From(settings, apiKey));
        }

        return null;
    }

    public bool TryExecute(string raw, ISlashOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '/')
        {
            return false;
        }

        var body = trimmed[1..];
        var space = body.IndexOf(' ');
        var name = space < 0 ? body : body[..space];
        var argument = space < 0 ? string.Empty : body[(space + 1)..].Trim();
        foreach (var command in Commands)
        {
            if (!string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            command.Execute(argument, output);
            return true;
        }

        return false;
    }

    public IEnumerable<string> Describe(bool enabled)
    {
        yield return $"Plugins ({Plugins.Count}, {(enabled ? "On" : "Off")})";
        foreach (var plugin in Plugins)
        {
            var source = plugin.Source == PluginSource.Home ? "Home" : "Project";
            yield return
                $"  {source}  {plugin.DirectoryName}  {plugin.Name}  "
                + $"tools {plugin.Tools}, commands {plugin.Commands}, hooks {plugin.Hooks}, "
                + $"raw hooks {plugin.RawHooks}";
        }
    }

    private static bool TryActivate(DiscoveredPlugin item, out CrystalCode.Plugins.IPlugin? plugin, out string error)
    {
        plugin = null;
        error = string.Empty;
        try
        {
            if (!File.Exists(item.Manifest.AssemblyPath))
            {
                error = "assembly was not found.";
                return false;
            }

            var entryName = Path.GetFileNameWithoutExtension(item.Manifest.AssemblyPath);
            if (PluginLoadContext.IsHostAssemblyName(entryName))
            {
                error = "host assemblies cannot be loaded as plugins.";
                return false;
            }

            var context = new PluginLoadContext(item.Manifest.AssemblyPath);
            var assembly = context.LoadFromAssemblyPath(item.Manifest.AssemblyPath);
            var type = assembly.GetType(item.Manifest.TypeName, throwOnError: false, ignoreCase: false);
            if (type is null
                || type.IsAbstract
                || type.IsInterface
                || !typeof(CrystalCode.Plugins.IPlugin).IsAssignableFrom(type))
            {
                error = $"type '{item.Manifest.TypeName}' does not implement IPlugin.";
                return false;
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                error = $"type '{item.Manifest.TypeName}' needs a public parameterless constructor.";
                return false;
            }

            if (Activator.CreateInstance(type) is not CrystalCode.Plugins.IPlugin created)
            {
                error = $"type '{item.Manifest.TypeName}' could not be created.";
                return false;
            }

            plugin = created;
            return true;
        }
        catch (Exception exception)
        {
            error = string.IsNullOrWhiteSpace(exception.Message)
                ? "the assembly could not be loaded."
                : exception.Message;
            return false;
        }
    }

    private static int AddTools(
        string directoryName,
        DiskContribution contribution,
        SessionToolHost host,
        HashSet<string> names,
        IList<string> notes,
        List<ITool> plan,
        List<ITool> work,
        List<IMultimodalTool> planMultimodal,
        List<IMultimodalTool> workMultimodal)
    {
        var added = 0;
        foreach (var contributionTool in contribution.Tools)
        {
            if (contributionTool is null)
            {
                notes.Add($"Plugin '{directoryName}' omitted a missing tool.");
                continue;
            }

            var name = contributionTool.Name?.Trim() ?? string.Empty;
            if (!ExternalToolNames.IsToolName(name) || !names.Add(name))
            {
                notes.Add($"Plugin '{directoryName}' omitted tool '{contributionTool.Name}'.");
                continue;
            }

            var tool = contributionTool.Tool;
            if (tool is IHostTool hosted)
            {
                tool = new PluginHostTool(hosted, host, directoryName);
            }

            if (tool is null || !string.Equals(tool.Definition.Name, name, StringComparison.Ordinal))
            {
                names.Remove(name);
                notes.Add($"Plugin '{directoryName}' omitted tool '{name}' because its definition name differs.");
                continue;
            }

            var catalogs = contributionTool.Catalogs;
            if (catalogs is null)
            {
                names.Remove(name);
                notes.Add($"Plugin '{directoryName}' omitted tool '{name}' because it has no catalogs.");
                continue;
            }

            if (catalogs.Contains(PluginToolCatalog.Work))
            {
                work.Add(tool);
            }

            if (catalogs.Contains(PluginToolCatalog.Plan))
            {
                plan.Add(tool);
            }

            var multimodal = contributionTool.Multimodal;
            if (multimodal is IHostMultimodalTool hostedMultimodal)
            {
                multimodal = new PluginHostMultimodalTool(hostedMultimodal, host, directoryName);
            }

            if (multimodal is not null)
            {
                if (string.Equals(multimodal.Definition.Name, name, StringComparison.Ordinal))
                {
                    if (catalogs.Contains(PluginToolCatalog.Work))
                    {
                        workMultimodal.Add(multimodal);
                    }

                    if (catalogs.Contains(PluginToolCatalog.Plan))
                    {
                        planMultimodal.Add(multimodal);
                    }
                }
                else
                {
                    notes.Add(
                        $"Plugin '{directoryName}' omitted multimodal tool '{multimodal.Definition.Name}' "
                        + $"because it must match '{name}'.");
                }
            }

            added++;
        }

        return added;
    }

    private static void AddPlaceholders(
        string directoryName,
        DiskContribution contribution,
        HashSet<string> names,
        IList<string> notes,
        List<PluginPlaceholderRegistration> placeholders)
    {
        foreach (var placeholder in contribution.Placeholders)
        {
            if (PluginPlaceholderAdmission.TryAdmit(
                    directoryName,
                    placeholder,
                    names,
                    notes,
                    out var registration)
                && registration is not null)
            {
                placeholders.Add(registration);
            }
        }
    }

    private static int AddCommands(
        string directoryName,
        DiskContribution contribution,
        HashSet<string> names,
        IList<string> notes,
        List<ISlashCommand> commands)
    {
        var added = 0;
        foreach (var command in contribution.Commands)
        {
            if (command is null || !IsCommandName(command.Name) || SlashCatalog.TryMatch(command.Name, out _))
            {
                notes.Add($"Plugin '{directoryName}' omitted command '{command?.Name}'.");
                continue;
            }

            if (!names.Add(command.Name.Trim()))
            {
                notes.Add($"Plugin '{directoryName}' omitted duplicate command '{command.Name}'.");
                continue;
            }

            commands.Add(new DiskCommand(command));
            added++;
        }

        return added;
    }

    private static int AddClients(
        string directoryName,
        DiskContribution contribution,
        IList<string> notes,
        List<IPluginClientFactory> clients)
    {
        var added = 0;
        foreach (var factory in contribution.Clients)
        {
            if (factory is null)
            {
                notes.Add($"Plugin '{directoryName}' omitted a missing client factory.");
                continue;
            }

            if (ClaimsBuiltInProtocol(factory, out var protocol))
            {
                notes.Add(
                    $"Plugin '{directoryName}' omitted a client factory because it claims built-in protocol '{protocol}'.");
                continue;
            }

            clients.Add(factory);
            added++;
        }

        return added;
    }

    private static int AddClassifiers(
        string directoryName,
        DiskContribution contribution,
        IList<string> notes,
        List<IApprovalClassifier> classifiers)
    {
        var added = 0;
        foreach (var classifier in contribution.Classifiers)
        {
            if (classifier is null)
            {
                notes.Add($"Plugin '{directoryName}' omitted a missing classifier.");
                continue;
            }

            classifiers.Add(new DiskClassifier(classifier));
            added++;
        }

        return added;
    }

    private static int AddHooks(
        string directoryName,
        DiskContribution contribution,
        IList<string> notes,
        List<IPluginHook> hooks)
    {
        var added = 0;
        foreach (var hook in contribution.Hooks)
        {
            if (hook is null)
            {
                notes.Add($"Plugin '{directoryName}' omitted a missing hook.");
                continue;
            }

            hooks.Add(hook);
            added++;
        }

        return added;
    }

    private static int AddRawHooks(
        string directoryName,
        DiskContribution contribution,
        IList<string> notes,
        List<IPluginRawHook> rawHooks)
    {
        var added = 0;
        foreach (var hook in contribution.RawHooks)
        {
            if (hook is null)
            {
                notes.Add($"Plugin '{directoryName}' omitted a missing raw hook.");
                continue;
            }

            rawHooks.Add(hook);
            added++;
        }

        if (added > 0)
        {
            notes.Add(
                added == 1
                    ? $"Plugin '{directoryName}' registered a raw hook."
                    : $"Plugin '{directoryName}' registered {added} raw hooks.");
        }

        return added;
    }

    private static bool ClaimsBuiltInProtocol(IPluginClientFactory factory, out string protocol)
    {
        foreach (var candidate in BuiltInProtocols)
        {
            try
            {
                if (factory.CanCreate(candidate))
                {
                    protocol = candidate;
                    return true;
                }
            }
            catch (Exception)
            {
                protocol = candidate;
                return true;
            }
        }

        protocol = string.Empty;
        return false;
    }

    private static bool IsCommandName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > PluginFiles.MaximumNameLength)
        {
            return false;
        }

        if (!char.IsAsciiLetter(name[0]))
        {
            return false;
        }

        for (var index = 1; index < name.Length; index++)
        {
            var character = name[index];
            if (!char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    private sealed record LoadedPlugin(string DirectoryName, CrystalCode.Plugins.IPlugin Plugin);
}
