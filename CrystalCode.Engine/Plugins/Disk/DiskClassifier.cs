using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Tools;
using CrystalCode.Plugins.Approvals;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>Adapts a disk classifier onto the host approval table.</summary>
internal sealed class DiskClassifier : IApprovalClassifier
{
    private readonly IPluginClassifier _inner;

    public DiskClassifier(IPluginClassifier inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public bool TryClassify(
        ToolCall call,
        Workspace workspace,
        out ToolClassification classification)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(workspace);
        classification = null!;
        if (!_inner.TryClassify(call, workspace.Root, out var plugin) || plugin is null)
        {
            return false;
        }

        classification = new ToolClassification(
            PluginRiskMap.ToRisk(plugin.Risk),
            PluginRiskMap.ToAuthority(plugin.Authority),
            plugin.Summary);
        return true;
    }
}
