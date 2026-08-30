using Crystal.Tools;

using CrystalHarness.Tools;

namespace CrystalHarness.Plugins;

/// <summary>
/// A tool factory registered on the in-process plugin table.
/// </summary>
public interface IToolContribution
{
    string Name { get; }

    bool IncludeInPlan { get; }

    ITool Create(Workspace workspace, TodoList todos, IUserPrompt prompt);
}
