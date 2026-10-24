using Crystal.Chat;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Tools;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Plugins.Providers;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// In-process table of plugin contributions. Does not load assemblies from disk.
/// </summary>
public sealed class PluginRegistry
{
    private readonly List<IToolContribution> _tools = [];
    private readonly Dictionary<string, IToolContribution> _replacements = new(StringComparer.Ordinal);
    private readonly List<IChatClientFactory> _clients = [];
    private readonly List<IApprovalClassifier> _classifiers = [];
    private readonly List<ISlashCommand> _commands = [];
    private readonly HashSet<string> _toolNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _commandNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pluginNames = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IToolContribution> Tools => [.. _tools, .. _replacements.Values];

    public IReadOnlyList<IChatClientFactory> Clients => _clients;

    public IReadOnlyList<IApprovalClassifier> Classifiers => _classifiers;

    public IReadOnlyList<ISlashCommand> Commands => _commands;

    public static PluginRegistry CreateBuiltIn()
    {
        var registry = new PluginRegistry();
        registry.Add(new WorkspaceToolsPlugin());
        registry.Add(new DeepSeekPlugin());
        registry.Add(new OpenAIPlugin());
        registry.Add(new ResponsesPlugin());
        registry.Add(new AnthropicPlugin());
        registry.Add(new GeminiPlugin());
        registry.Add(new OllamaPlugin());
        return registry;
    }

    public void Add(IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (string.IsNullOrWhiteSpace(plugin.Name))
        {
            throw new ArgumentException("Plugin name is required.", nameof(plugin));
        }

        var name = plugin.Name.Trim();
        if (!_pluginNames.Add(name))
        {
            throw new InvalidOperationException(
                $"Plugin '{name}' is already registered.");
        }

        var contribution = plugin.Contribute();
        ArgumentNullException.ThrowIfNull(contribution);
        foreach (var tool in contribution.Tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (tool.Kind == ToolContributionKind.Replace)
            {
                AddReplacement(name, tool);
                continue;
            }

            if (tool.Kind != ToolContributionKind.Append)
            {
                throw new InvalidOperationException(
                    $"Plugin '{name}' contributed tool '{tool.Name}' with an unsupported contribution kind.");
            }

            if (!_toolNames.Add(tool.Name))
            {
                throw new InvalidOperationException(
                    $"Plugin '{name}' contributed a duplicate tool '{tool.Name}'.");
            }

            _tools.Add(tool);
        }

        foreach (var client in contribution.Clients)
        {
            ArgumentNullException.ThrowIfNull(client);
            _clients.Add(client);
        }

        foreach (var classifier in contribution.Classifiers)
        {
            ArgumentNullException.ThrowIfNull(classifier);
            _classifiers.Add(classifier);
        }

        foreach (var command in contribution.Commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (string.IsNullOrWhiteSpace(command.Name)
                || !_commandNames.Add(command.Name.Trim()))
            {
                throw new InvalidOperationException(
                    $"Plugin '{name}' contributed a duplicate command '{command.Name}'.");
            }

            _commands.Add(command);
        }
    }

    public IReadOnlyList<ITool> CreateTools(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt,
        HostToolCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        if (!Enum.IsDefined(catalog))
        {
            throw new ArgumentOutOfRangeException(nameof(catalog));
        }

        var tools = new List<ITool>();
        foreach (var contribution in _tools)
        {
            var selected = SelectContribution(contribution, catalog);
            if (!selected.Catalogs.Contains(catalog))
            {
                continue;
            }

            tools.Add(ValidateTool(selected, selected.Create(workspace, todos, prompt)));
        }

        return tools;
    }

    private void AddReplacement(string pluginName, IToolContribution contribution)
    {
        var target = contribution.ReplacementTarget;
        if (string.IsNullOrWhiteSpace(target)
            || !string.Equals(target, contribution.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginName}' contributed replacement '{contribution.Name}' without a matching target.");
        }

        if (!ExternalToolNames.IsReserved(target) || !_toolNames.Contains(target))
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginName}' cannot replace unknown built-in tool '{target}'.");
        }

        if (!_replacements.TryAdd(target, contribution))
        {
            throw new InvalidOperationException(
                $"Built-in tool '{target}' already has a replacement.");
        }
    }

    private IToolContribution SelectContribution(
        IToolContribution contribution,
        HostToolCatalog catalog)
    {
        if (_replacements.TryGetValue(contribution.Name, out var replacement)
            && replacement.Catalogs.Contains(catalog))
        {
            return replacement;
        }

        return contribution;
    }

    private static ITool ValidateTool(
        IToolContribution contribution,
        ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!string.Equals(tool.Definition.Name, contribution.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Tool contribution '{contribution.Name}' returned definition '{tool.Definition.Name}'.");
        }

        return tool;
    }

    public IReadOnlyList<IMultimodalTool> CreateMultimodalTools(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt,
        HostToolCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        if (!Enum.IsDefined(catalog))
        {
            throw new ArgumentOutOfRangeException(nameof(catalog));
        }

        var tools = new List<IMultimodalTool>();
        foreach (var contribution in _tools)
        {
            var selected = SelectContribution(contribution, catalog);
            if (!selected.Catalogs.Contains(catalog))
            {
                continue;
            }

            var tool = selected.CreateMultimodal(workspace, todos, prompt);
            if (tool is not null)
            {
                if (!string.Equals(
                        tool.Definition.Name,
                        selected.Name,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Multimodal tool '{tool.Definition.Name}' must match contribution '{selected.Name}'.");
                }

                tools.Add(tool);
            }
        }

        return tools;
    }

    public IStreamingChatClient CreateClient(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        }
        var protocol = settings.ActiveProvider.Protocol;
        foreach (var factory in _clients)
        {
            if (factory.CanCreate(protocol))
            {
                return factory.Create(settings, apiKey);
            }
        }

        throw new NotSupportedException(
            $"Provider protocol '{protocol.Value}' is not supported.");
    }

    public IStreamingMultimodalChatClient? CreateMultimodalClient(
        HarnessSettings settings,
        string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        }
        if (!settings.ActiveModel.ImageInput)
        {
            return null;
        }

        var protocol = settings.ActiveProvider.Protocol;
        foreach (var factory in _clients)
        {
            if (factory.CanCreate(protocol))
            {
                return factory.CreateMultimodal(settings, apiKey);
            }
        }

        return null;
    }

    public ISlashCommand? FindCommand(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = name.Trim();
        foreach (var command in _commands)
        {
            if (string.Equals(command.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                return command;
            }
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
        var command = FindCommand(name);
        if (command is null)
        {
            return false;
        }

        command.Execute(argument, output);
        return true;
    }
}
