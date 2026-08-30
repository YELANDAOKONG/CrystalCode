using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Configuration;
using CrystalHarness.Plugins.Providers;
using CrystalHarness.Tools;

namespace CrystalHarness.Plugins;

/// <summary>
/// In-process table of plugin contributions. Does not load assemblies from disk.
/// </summary>
public sealed class PluginRegistry
{
    private readonly List<IToolContribution> _tools = [];
    private readonly List<IChatClientFactory> _clients = [];
    private readonly List<IApprovalClassifier> _classifiers = [];
    private readonly List<ISlashCommand> _commands = [];
    private readonly HashSet<string> _toolNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _commandNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pluginNames = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IToolContribution> Tools => _tools;

    public IReadOnlyList<IChatClientFactory> Clients => _clients;

    public IReadOnlyList<IApprovalClassifier> Classifiers => _classifiers;

    public IReadOnlyList<ISlashCommand> Commands => _commands;

    public static PluginRegistry CreateBuiltIn()
    {
        var registry = new PluginRegistry();
        registry.Add(new WorkspaceToolsPlugin());
        registry.Add(new DeepSeekPlugin());
        registry.Add(new OpenAIPlugin());
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
        bool plan)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        var tools = new List<ITool>();
        foreach (var contribution in _tools)
        {
            if (plan && !contribution.IncludeInPlan)
            {
                continue;
            }

            tools.Add(contribution.Create(workspace, todos, prompt));
        }

        return tools;
    }

    public IStreamingChatClient CreateClient(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
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
}
