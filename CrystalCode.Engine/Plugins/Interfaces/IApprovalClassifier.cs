using Crystal.Tools;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Plugins.Interfaces;

/// <summary>
/// Classifies a tool the built-in switch does not know.
/// </summary>
public interface IApprovalClassifier
{
    bool TryClassify(
        ToolCall call,
        Workspace workspace,
        out ToolClassification classification);
}
