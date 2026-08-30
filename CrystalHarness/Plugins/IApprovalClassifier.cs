using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Tools;

namespace CrystalHarness.Plugins;

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
