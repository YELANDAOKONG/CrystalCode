using Crystal.Tools;
using Crystal.Multimodal.Tools;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Tool contribution backed by a create delegate.
/// </summary>
public sealed class FactoryToolContribution : IToolContribution
{
    private readonly Func<Workspace, TodoList, IUserPrompt, ITool> _create;
    private readonly Func<Workspace, TodoList, IUserPrompt, IMultimodalTool?>?
        _createMultimodal;

    public FactoryToolContribution(
        string name,
        HostToolCatalogs catalogs,
        Func<Workspace, TodoList, IUserPrompt, ITool> create,
        Func<Workspace, TodoList, IUserPrompt, IMultimodalTool?>?
            createMultimodal = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(create);
        Name = name.Trim();
        Catalogs = catalogs;
        _create = create;
        _createMultimodal = createMultimodal;
    }

    public string Name { get; }

    public HostToolCatalogs Catalogs { get; }

    public ITool Create(Workspace workspace, TodoList todos, IUserPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return _create(workspace, todos, prompt);
    }

    public IMultimodalTool? CreateMultimodal(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return _createMultimodal?.Invoke(workspace, todos, prompt);
    }
}
