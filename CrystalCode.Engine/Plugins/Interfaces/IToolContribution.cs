using Crystal.Tools;
using Crystal.Multimodal.Tools;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Plugins.Interfaces;

/// <summary>
/// A tool factory registered on the in-process plugin table.
/// </summary>
public interface IToolContribution
{
    string Name { get; }

    HostToolCatalogs Catalogs { get; }

    ITool Create(Workspace workspace, TodoList todos, IUserPrompt prompt);

    IMultimodalTool? CreateMultimodal(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt) => null;
}
