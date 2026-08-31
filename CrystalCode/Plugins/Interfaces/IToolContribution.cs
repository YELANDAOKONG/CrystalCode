using Crystal.Tools;
using CrystalCode.Tools;

namespace CrystalCode.Plugins.Interfaces;

/// <summary>
/// A tool factory registered on the in-process plugin table.
/// </summary>
public interface IToolContribution
{
    string Name { get; }

    bool IncludeInPlan { get; }

    ITool Create(Workspace workspace, TodoList todos, IUserPrompt prompt);
}
