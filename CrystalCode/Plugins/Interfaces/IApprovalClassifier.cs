using Crystal.Tools;
using CrystalCode.Approvals;
using CrystalCode.Tools;

namespace CrystalCode.Plugins.Interfaces;

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
