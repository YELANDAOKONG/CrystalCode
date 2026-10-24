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
            createMultimodal = null,
        ToolContributionKind kind = ToolContributionKind.Append,
        string? replacementTarget = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(create);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tool contribution kind is not defined.");
        }

        if (kind == ToolContributionKind.Replace)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(replacementTarget);
            if (!string.Equals(name.Trim(), replacementTarget.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A replacement target must match the contributed tool name.",
                    nameof(replacementTarget));
            }
        }
        else if (!string.IsNullOrWhiteSpace(replacementTarget))
        {
            throw new ArgumentException(
                "An appended tool cannot declare a replacement target.",
                nameof(replacementTarget));
        }

        Name = name.Trim();
        Catalogs = catalogs;
        Kind = kind;
        ReplacementTarget = string.IsNullOrWhiteSpace(replacementTarget)
            ? null
            : replacementTarget.Trim();
        _create = create;
        _createMultimodal = createMultimodal;
    }

    public string Name { get; }

    public HostToolCatalogs Catalogs { get; }

    public ToolContributionKind Kind { get; }

    public string? ReplacementTarget { get; }

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
