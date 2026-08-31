using Crystal.Tools;

using CrystalHarness.Plugins.Interfaces;
using CrystalHarness.Tools;

namespace CrystalHarness.Plugins;

/// <summary>
/// Tool contribution backed by a create delegate.
/// </summary>
public sealed class FactoryToolContribution : IToolContribution
{
    private readonly Func<Workspace, TodoList, IUserPrompt, ITool> _create;

    public FactoryToolContribution(
        string name,
        bool includeInPlan,
        Func<Workspace, TodoList, IUserPrompt, ITool> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(create);
        Name = name.Trim();
        IncludeInPlan = includeInPlan;
        _create = create;
    }

    public string Name { get; }

    public bool IncludeInPlan { get; }

    public ITool Create(Workspace workspace, TodoList todos, IUserPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return _create(workspace, todos, prompt);
    }
}
